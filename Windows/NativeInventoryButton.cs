using System;
using Dalamud.Game.Addon;
using Dalamud.Game.Addon.Events;
using Dalamud.Game.Addon.Events.EventDataTypes;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Component.GUI;
using NodeFlags = FFXIVClientStructs.FFXIV.Component.GUI.NodeFlags;

namespace AutoTrash.Windows;

/// <summary>
/// 背包原生界面注入的「垃圾桶」按钮（F2 / Plan A：手动 AtkUnitBase 注入，无第三方依赖）。
///
/// 机制（三阶段闭环，缺一不可）：
///  - PostSetup（注入）：通过 IAddonLifecycle 监听背包 addon（<see cref="TargetAddonName"/>）的 PostSetup，
///    在 addon 节点树就绪后注入一个 AtkImageNode（垃圾桶图标）。
///  - PreFinalize（摘除）：监听同一 addon 的 PreFinalize，在 addon 销毁前调用 <see cref="DetachNode"/>，
///    解链兄弟节点 + 移除原生事件 + Free 节点内存 + 将 injectedNode 复位为 null。
///    缺此环会导致 injectedNode 成为悬空非空指针，下次 PostSetup 命中 `if (injectedNode != null) return;`
///    使按钮在「背包关闭再打开」后永久消失。
///  - Dispose（兜底）：插件卸载时 <see cref="Disable"/> 同样会注销监听并强制 <see cref="DetachNode"/>。
///  - 通过 IAddonEventManager 为注入节点注册 MouseClick 原生事件，
///    点击即打开主窗口（MainWindow.Toggle()，与 Enabled 自动丢弃开关完全解耦）。
///
/// 运行时验证项（以 const 形式集中，便于游戏内核对 / 调整）：
///  - <see cref="TargetAddonName"/>：不同版本 / 语言下背包 addon 名可能不同（常见 "Inventory"）。
///  - <see cref="TrashIconId"/>：垃圾桶图标 ID，需确认具体编号，当前为占位值。
///  - <see cref="InjectedNodeId"/> / <see cref="ButtonX"/> / <see cref="ButtonY"/>：注入节点 ID 与相对位置。
/// </summary>
public sealed unsafe class InventoryTrashButton : IDisposable
{
    /// <summary>目标背包 addon 名（运行时需验证；不同版本/语言可能不同，常见为 "Inventory"）。</summary>
    private const string TargetAddonName = "Inventory";

    /// <summary>注入图标节点 ID（使用高位自定义 ID，避免与游戏节点冲突）。</summary>
    private const uint InjectedNodeId = 990001u;

    /// <summary>垃圾桶图标 ID（运行时需验证：需确认具体图标编号，当前为占位值 0）。</summary>
    private const uint TrashIconId = 0u;

    /// <summary>按钮相对 addon 根节点的 X 偏移（运行时可微调）。</summary>
    private const float ButtonX = 200f;

    /// <summary>按钮相对 addon 根节点的 Y 偏移（运行时可微调）。</summary>
    private const float ButtonY = 10f;

    private readonly Plugin plugin;
    private readonly IAddonLifecycle addonLifecycle;
    private readonly IAddonEventManager addonEventManager;

    private AtkImageNode* injectedNode;
    private IAddonEventHandle? eventHandle;
    private bool active;

    public InventoryTrashButton(Plugin plugin, IAddonLifecycle addonLifecycle, IAddonEventManager addonEventManager)
    {
        this.plugin = plugin;
        this.addonLifecycle = addonLifecycle;
        this.addonEventManager = addonEventManager;
        this.injectedNode = null;
    }

    /// <summary>启用：注册 addon 生命周期监听，等待背包 addon PostSetup 注入按钮。</summary>
    public void Enable()
    {
        if (active)
        {
            return;
        }

        if (!plugin.Configuration.ShowInventoryButton)
        {
            return;
        }

        active = true;
        addonLifecycle.RegisterListener(AddonEvent.PostSetup, TargetAddonName, OnAddonSetup);
        addonLifecycle.RegisterListener(AddonEvent.PreFinalize, TargetAddonName, OnAddonFinalize);
    }

    /// <summary>禁用：注销监听并摘除注入的节点 / 事件。</summary>
    public void Disable()
    {
        if (!active)
        {
            return;
        }

        active = false;
        addonLifecycle.UnregisterListener(AddonEvent.PostSetup, TargetAddonName, OnAddonSetup);
        addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, TargetAddonName, OnAddonFinalize);
        DetachNode();
    }

    public void Dispose()
    {
        Disable();
    }

    // IAddonLifecycle.AddonEventDelegate 签名：(AddonEvent eventType, AddonArgs args)
    private void OnAddonSetup(AddonEvent eventType, AddonArgs args)
    {
        if (eventType != AddonEvent.PostSetup)
        {
            return;
        }

        if (args.AddonName != TargetAddonName)
        {
            return;
        }

        if (!plugin.Configuration.ShowInventoryButton)
        {
            return;
        }

        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null)
        {
            return;
        }

        InjectButton(addon);
    }

    // IAddonLifecycle.AddonEventDelegate 签名：(AddonEvent eventType, AddonArgs args)
    // 闭环第 2 环：addon 即将销毁（背包关闭/重开）时，摘除注入节点、释放事件与内存，
    // 确保 injectedNode 复位为 null，下次 PostSetup 注入不会被 `if (injectedNode != null) return;` 拦截。
    private void OnAddonFinalize(AddonEvent eventType, AddonArgs args)
    {
        if (eventType != AddonEvent.PreFinalize)
        {
            return;
        }

        if (args.AddonName != TargetAddonName)
        {
            return;
        }

        DetachNode();
    }

    private void InjectButton(AtkUnitBase* addon)
    {
        // 防重复注入（PostSetup 可能多次触发）
        if (injectedNode != null)
        {
            return;
        }

        var uiSpace = IMemorySpace.GetUISpace();
        if (uiSpace == null)
        {
            return;
        }

        // Create 内部会 Malloc + Memset(0) + Ctor，无需再手动调用 Ctor
        var node = uiSpace->Create<AtkImageNode>();
        if (node == null)
        {
            return;
        }

        node->NodeId = InjectedNodeId;
        node->X = ButtonX;
        node->Y = ButtonY;
        node->NodeFlags = NodeFlags.Visible | NodeFlags.Enabled | NodeFlags.HasCollision | NodeFlags.RespondToMouse | NodeFlags.EmitsEvents;

        // 加载垃圾桶图标（language=0 表示随游戏语言）
        node->LoadIconTexture(TrashIconId, 0);

        // 手动链接到 addon 根节点子链头部
        var root = addon->RootNode;
        if (root != null)
        {
            node->ParentNode = root;
            node->PrevSiblingNode = root->ChildNode;
            if (root->ChildNode != null)
            {
                root->ChildNode->NextSiblingNode = (AtkResNode*)node;
            }

            root->ChildNode = (AtkResNode*)node;
        }

        injectedNode = node;

        // 注册 MouseClick：点击打开主窗口
        eventHandle = addonEventManager.AddEvent((nint)addon, (nint)node, AddonEventType.MouseClick, OnButtonClick);
    }

    private void DetachNode()
    {
        if (eventHandle != null)
        {
            addonEventManager.RemoveEvent(eventHandle);
            eventHandle = null;
        }

        if (injectedNode == null)
        {
            return;
        }

        var node = injectedNode;

        // 从父节点子链摘除
        var parent = node->ParentNode;
        if (parent != null && parent->ChildNode == node)
        {
            parent->ChildNode = node->NextSiblingNode;
        }

        if (node->PrevSiblingNode != null)
        {
            node->PrevSiblingNode->NextSiblingNode = node->NextSiblingNode;
        }

        if (node->NextSiblingNode != null)
        {
            node->NextSiblingNode->PrevSiblingNode = node->PrevSiblingNode;
        }

        node->ParentNode = null;
        node->PrevSiblingNode = null;
        node->NextSiblingNode = null;

        // 释放节点内存（与 Create 对应）
        IMemorySpace.Free(node);

        injectedNode = null;
    }

    // IAddonEventManager.AddonEventDelegate 签名：(AddonEventType eventType, AddonEventData data)
    private void OnButtonClick(AddonEventType eventType, AddonEventData data)
    {
        if (eventType != AddonEventType.MouseClick)
        {
            return;
        }

        // 打开主窗口（与 Enabled 自动丢弃开关解耦）
        plugin.MainWindow.Toggle();
    }
}
