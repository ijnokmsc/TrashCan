using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using Dalamud.Game.Inventory;
using FFXIVClientStructs.FFXIV.Client.Game;
using AutoTrash.Core;
using AutoTrash.Models;

namespace AutoTrash.Services;

/// <summary>
/// 一次待丢弃任务（已解析为容器/槽位/数量/HQ）。
/// </summary>
public class PendingDiscard
{
    /// <summary>原生 InventoryType 数值。</summary>
    public int Container { get; set; }

    /// <summary>槽位（ushort）。</summary>
    public ushort Slot { get; set; }

    /// <summary>物品 ItemId。</summary>
    public uint ItemId { get; set; }

    /// <summary>数量。</summary>
    public int Quantity { get; set; }

    /// <summary>是否 HQ。</summary>
    public bool IsHq { get; set; }
}

/// <summary>
/// 丢弃编排：在 IFramework.Update 安全帧内按规则（Enabled/列表命中/HQ/数量策略）执行丢弃。
/// </summary>
public class AutoDiscardService : IDisposable
{
    private readonly Configuration config;
    private readonly DiscardExecutor executor;
    private readonly LogStore logStore;
    private readonly IClientState clientState;
    private readonly IFramework framework;
    private readonly TrashListStore trashListStore;
    private readonly ItemResolver itemResolver;
    private readonly IGameInventory gameInventory;

    private readonly Queue<PendingDiscard> pending = new();
    private readonly object lockObj = new();
    private DateTime lastDiscard = DateTime.MinValue;
    private readonly TimeSpan discardInterval = TimeSpan.FromMilliseconds(300);

    /// <summary>暂停标记：主窗口打开期间置 true，停止执行任何自动/定时/扫描丢弃；关闭后恢复为 false。</summary>
    public bool Paused { get; set; } = false;

    public AutoDiscardService(Configuration config, DiscardExecutor executor, LogStore logStore, IClientState clientState, IFramework framework, TrashListStore trashListStore, ItemResolver itemResolver, IGameInventory gameInventory)
    {
        this.config = config;
        this.executor = executor;
        this.logStore = logStore;
        this.clientState = clientState;
        this.framework = framework;
        this.trashListStore = trashListStore;
        this.itemResolver = itemResolver;
        this.gameInventory = gameInventory;
        framework.Update += OnFrameworkUpdate;
    }

    /// <summary>入队一个待丢弃任务（同容器同槽位去重）。</summary>
    public void Enqueue(PendingDiscard item)
    {
        if (item == null)
        {
            return;
        }

        lock (lockObj)
        {
            foreach (var p in pending)
            {
                if (p.Container == item.Container && p.Slot == item.Slot)
                {
                    return;
                }
            }

            pending.Enqueue(item);
        }
    }

    /// <summary>框架帧回调：节流处理队列中的一项。</summary>
    private void OnFrameworkUpdate(IFramework fw)
    {
        if (!config.Enabled || !clientState.IsLoggedIn || Paused)
        {
            return;
        }

        if ((DateTime.Now - lastDiscard) < discardInterval)
        {
            return;
        }

        PendingDiscard? item = null;
        lock (lockObj)
        {
            if (pending.Count > 0)
            {
                item = pending.Dequeue();
            }
        }

        if (item == null)
        {
            return;
        }

        Process(item);
        lastDiscard = DateTime.Now;
    }

    /// <summary>规则判定并执行原生丢弃。</summary>
    private void Process(PendingDiscard item)
    {
        if (!config.Enabled)
        {
            return;
        }

        // 暂停期间不执行任何丢弃（主窗口打开时由 MainWindow 置位，防御性兜底）
        if (Paused)
        {
            return;
        }

        var container = (InventoryType)item.Container;

        // 容器白名单（仅背包 1~4）
        if (!Constants.IsEligibleContainer(container))
        {
            return;
        }

        // 列表命中判定（核心闸门）：仅列表内物品才进入丢弃流程
        var itemName = itemResolver.GetName(item.ItemId);
        if (!trashListStore.Contains(item.ItemId, itemName))
        {
            return;
        }

        // 特殊物品（装备）类型保护：默认拦截，需用户显式允许才丢
        if (config.ProtectSpecialItems && !config.AllowDiscardEquip && itemResolver.IsEquipItem(item.ItemId))
        {
            return;
        }

        // HQ 保护
        if (item.IsHq && !config.DiscardHq)
        {
            return;
        }

        // 重新定位目标物品当前槽位（丢弃后背包压缩会让原槽位失效）。
        // 安全回退策略：
        //   容器可读但目标已不在该容器 -> 返回 -1，视为已移走/上轮已丢，安全跳过，
        //        避免误丢旧槽位上已被压缩后占据的未授权物品（Bug 2 的核心隐患）。
        //   容器瞬时不可访问（读取抛异常）-> 无法验证，回退到扫描时记录的原始槽位
        //        (fallbackSlot = item.Slot) 继续丢弃，沿用旧口径、不冒险跳过。
        // 条目级阈值反查：命中条目且启用阈值 → 强制 KeepBelowThreshold + 条目阈值；
        // 否则回退全局策略（config.Mode / config.QuantityThreshold）。
        // 旧列表（无 HasThreshold 字段）反序列化默认 false，故完全走全局，行为不变（零回归）。
        var entry = trashListStore.GetEntry(item.ItemId, itemName);
        var effectiveMode = ResolveMode(entry);
        var effectiveThreshold = ResolveThreshold(entry);

        var discardSlot = ResolveCurrentSlot(container, item.ItemId, item.IsHq, item.Slot);
        if (discardSlot < 0)
        {
            // 物品已不在容器：跳过，交由下次扫描自愈，避免误丢未授权物品。
            return;
        }

        int discardResult;
        switch (effectiveMode)
        {
            case QuantityMode.DiscardAll:
                discardResult = executor.Discard(container, (ushort)discardSlot);
                break;

            case QuantityMode.DiscardAboveThreshold:
                if (item.Quantity > effectiveThreshold)
                {
                    discardResult = executor.Discard(container, (ushort)discardSlot);
                }
                else
                {
                    return;
                }

                break;

            case QuantityMode.KeepBelowThreshold:
                if (item.Quantity > effectiveThreshold)
                {
                    // SplitAndDiscard 仅丢超出部分（excess = 当前数量 - 保留阈值），按 slot 安全判定，绝不超丢。
                    var excess = item.Quantity - effectiveThreshold;
                    discardResult = executor.SplitAndDiscard(container, (ushort)discardSlot, excess);
                }
                else
                {
                    return;
                }

                break;

            default:
                discardResult = executor.Discard(container, (ushort)discardSlot);
                break;
        }

        // 仅在“实际执行丢弃且原生返回 0（成功）”时记录日志；
        // 所有跳过（容器/列表/装备/HQ/阈值未达）与失败均不记录，避免日志被无意义的跳过信息刷屏。
        if (discardResult == 0)
        {
            var kept = effectiveMode == QuantityMode.KeepBelowThreshold ? effectiveThreshold : 0;
            var note = effectiveMode == QuantityMode.KeepBelowThreshold
                ? $"保留 {kept}，仅丢超出部分：{itemName} x{item.Quantity - kept}"
                : $"丢弃 {itemName} x{item.Quantity}";
            logStore.Append(new DiscardLogEntry(
                DateTime.Now,
                item.ItemId,
                itemName,
                (uint)item.Quantity,
                item.Container,
                true,
                note));
        }
    }

    /// <summary>
    /// 解析条目生效模式：条目启用阈值（HasThreshold=true）→ 强制 KeepBelowThreshold；
    /// 否则回退全局 config.Mode。保证「条目级保留 N」语义，且不改变无阈值条目的旧行为。
    /// </summary>
    private QuantityMode ResolveMode(TrashItemEntry? entry)
    {
        if (entry != null && entry.HasThreshold)
        {
            return QuantityMode.KeepBelowThreshold;
        }

        return config.Mode;
    }

    /// <summary>
    /// 解析条目生效阈值：条目启用阈值（HasThreshold=true）→ 条目 QuantityThreshold；
    /// 否则回退全局 config.QuantityThreshold。
    /// </summary>
    private int ResolveThreshold(TrashItemEntry? entry)
    {
        if (entry != null && entry.HasThreshold)
        {
            return entry.QuantityThreshold;
        }

        return config.QuantityThreshold;
    }

    /// <summary>重新定位目标物品在当前容器内的真实槽位。背包每次丢弃后会自动压缩，
    /// 扫描时记录的槽位可能已失效；这里按 ItemId + IsHq 重新查找，确保丢弃的是正确物品。</summary>
    /// <param name="container">原生 InventoryType。</param>
    /// <param name="itemId">目标物品 ItemId。</param>
    /// <param name="isHq">是否 HQ。</param>
    /// <param name="fallbackSlot">扫描时记录的原始槽位；容器瞬时不可访问时回退使用，不冒险跳过。</param>
    /// <returns>
    /// 容器内匹配 ItemId+IsHq 的非空物品槽位；
    /// 容器可读但遍历完未找到匹配物品 -> -1（物品已不在该容器，交由调用方安全跳过）；
    /// 容器读取过程抛异常（瞬时不可访问）-> fallbackSlot（无法验证，沿用旧口径继续丢弃）。
    /// </returns>
    private int ResolveCurrentSlot(InventoryType container, uint itemId, bool isHq, int fallbackSlot)
    {
        try
        {
            var items = gameInventory.GetInventoryItems((GameInventoryType)(int)container);
            foreach (var it in items)
            {
                if (!it.IsEmpty && it.ItemId == itemId && it.IsHq == isHq)
                {
                    return (int)it.InventorySlot;
                }
            }

            // 容器可读，但目标物品已不在该容器：返回 -1 交由调用方安全跳过，
            // 避免误丢旧槽位上压缩后占据的未授权物品（Bug 2 核心隐患）。
            return -1;
        }
        catch
        {
            // 容器瞬时不可访问（读取过程抛异常）：无法验证，回退到扫描时记录的原始槽位，
            // 不冒险跳过，沿用旧口径。
            return fallbackSlot;
        }
    }

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
    }
}
