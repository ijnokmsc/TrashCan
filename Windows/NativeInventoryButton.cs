using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Events;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoTrash.Windows;

/// <summary>
/// F2「背包垃圾桶按钮」——安全版（纯 ImGui 覆盖层，不再操作原生节点树）。
///
/// 历史：初版实现手动 Create&lt;AtkImageNode&gt; 并 LoadIconTexture 注入到背包 addon 节点树，
/// 但 freshly-created AtkImageNode 的 AtkTexture* Tex 未初始化，LoadIconTexture 解引用空 Tex
/// 导致游戏 C0000005 崩溃（见 2026-07-13 实机 crash dump，调用栈落在 InjectButton）。
/// 磁盘上无任何插件对「自建节点」调用 LoadIconTexture，均只对游戏布局已初始化的节点调用。
///
/// 现改方案：纯托管 ImGui 窗口，当背包 addon 处于打开状态时显示，点击打开 AutoTrash 主窗口。
/// 窗口默认吸附在背包左侧；按住右键可拖动，松开后若启用吸附则自动回到背包左侧。
/// 完全不触碰原生内存，绝无崩溃风险。
/// </summary>
public sealed class InventoryTrashButton : IDisposable
{
    /// <summary>候选背包 addon 名（不同版本/语言可能不同，常见为 "Inventory"）。</summary>
    private static readonly string[] CandidateAddonNames =
    {
        "Inventory", "InventoryLarge", "InventoryGrid", "InventoryExpansion",
    };

    /// <summary>FontAwesome 垃圾桶图标 ID 占位符（默认回退）。</summary>
    private const FontAwesomeIcon DefaultTrashIcon = FontAwesomeIcon.TrashAlt;

    /// <summary>图标按钮的基础边长（像素）。最终尺寸会乘以用户配置的缩放。</summary>
    private const float BaseButtonSize = 40f;

    /// <summary>按钮与背包左侧的间隔（像素）。</summary>
    private const float AnchorGap = 8f;

    /// <summary>窗口内边距（像素）。</summary>
    private const float WindowPaddingBase = 6f;

    private readonly Plugin plugin;

    /// <summary>缓存的游戏图标共享纹理（避免每帧重新查询，由 TryGetWrap 实际取 wrap）。</summary>
    private ISharedImmediateTexture? iconTexture;

    private uint lastIconId;

    private bool isDragging;

    public InventoryTrashButton(Plugin plugin, IAddonLifecycle _, IAddonEventManager __)
    {
        this.plugin = plugin;
    }

    /// <summary>启用：覆盖层无需注册原生监听，保持接口兼容（Plugin.cs 仍调用 Enable）。</summary>
    public void Enable()
    {
    }

    /// <summary>禁用：覆盖层无需摘除原生节点。</summary>
    public void Disable()
    {
    }

    public void Dispose()
    {
        Disable();
        iconTexture = null;
    }

    /// <summary>每帧由 Plugin.Draw 调用：背包打开时绘制可点击的垃圾桶按钮。</summary>
    public unsafe void Draw()
    {
        if (!plugin.Configuration.ShowInventoryButton)
        {
            return;
        }

        var config = plugin.Configuration;
        var scale = config.InventoryButtonScale;
        var size = new Vector2(BaseButtonSize * scale, BaseButtonSize * scale);
        var padding = WindowPaddingBase * scale;
        var gap = AnchorGap * scale;
        var windowPadding = new Vector2(padding, padding);
        var windowSize = size + windowPadding * 2f;

        var alwaysShow = config.InventoryButtonAlwaysShow;
        Vector2 position;
        if (alwaysShow)
        {
            // 常驻模式：按钮始终可见。背包打开时吸附到背包左侧并跟随；背包关闭时回到原位（右上角固定点）。
            if (TryGetInventoryAddon(out var addon))
            {
                position = new Vector2(addon->X - windowSize.X - gap, addon->Y + gap) + config.InventoryButtonOffset;
            }
            else
            {
                var vp = ImGui.GetMainViewport();
                var basePos = new Vector2(vp.Pos.X + vp.Size.X - windowSize.X - 16f, vp.Pos.Y + 16f);
                position = basePos + config.InventoryButtonOffset;
            }
        }
        else
        {
            if (!TryGetInventoryAddon(out var addon))
            {
                return;
            }

            // 吸附锚点：背包左侧（按钮右边紧贴背包左边，垂直与背包顶部对齐）。
            var anchor = new Vector2(addon->X - windowSize.X - gap, addon->Y + gap);
            position = anchor + config.InventoryButtonOffset;
        }

        // 夹取到主视口内，确保按钮窗口永不跑出屏幕（即便背包坐标为异常值也能看到按钮）。
        var viewport = ImGui.GetMainViewport();
        var screen = viewport.Size;
        var clampedX = Math.Clamp(position.X, 0f, Math.Max(0f, screen.X - windowSize.X));
        var clampedY = Math.Clamp(position.Y, 0f, Math.Max(0f, screen.Y - windowSize.Y));
        position = new Vector2(clampedX, clampedY);

        ImGui.SetNextWindowSize(windowSize, ImGuiCond.Always);
        ImGui.SetNextWindowPos(position, ImGuiCond.Always);

        var flags = ImGuiWindowFlags.NoTitleBar
                  | ImGuiWindowFlags.NoResize
                  | ImGuiWindowFlags.NoScrollbar
                  | ImGuiWindowFlags.NoScrollWithMouse
                  | ImGuiWindowFlags.NoBackground
                  | ImGuiWindowFlags.NoSavedSettings;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, windowPadding);
        ImGui.Begin("AutoTrashIcon###InvTrashIcon", flags);

        // 右键拖动：按住右键时拖动整个小窗。
        var hovered = ImGui.IsWindowHovered();
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && hovered)
        {
            isDragging = true;
        }

        if (isDragging)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Right))
            {
                var io = ImGui.GetIO();
                config.InventoryButtonOffset += io.MouseDelta;
            }
            else
            {
                isDragging = false;
                if (config.InventoryButtonSnapLeft)
                {
                    // 保留用户垂直方向的微调，水平方向回到吸附位置。
                    config.InventoryButtonOffset = new Vector2(0f, config.InventoryButtonOffset.Y);
                }

                config.Save();
            }
        }

        ImGui.SetCursorPos(windowPadding);
        var clicked = DrawIconButton(size);
        if (clicked)
        {
            plugin.MainWindow.Toggle();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("打开 AutoTrash\n按住右键可拖动位置");
        }

        ImGui.End();
        ImGui.PopStyleVar();
    }

    /// <summary>绘制图标按钮：优先使用配置的游戏图标 ID，否则回退 FontAwesome 垃圾桶。</summary>
    private bool DrawIconButton(Vector2 size)
    {
        var iconId = plugin.Configuration.InventoryButtonIconId;

        if (iconId != 0)
        {
            var wrap = GetOrLoadGameIcon(iconId);
            if (wrap != null)
            {
                return ImGui.ImageButton(wrap.Handle, size);
            }
        }

        // 回退：FontAwesome 图标按钮。ImGuiComponents 内部会切换字体，渲染完即恢复。
        return ImGuiComponents.IconButton(DefaultTrashIcon, size);
    }

    /// <summary>获取指定游戏图标的可渲染 wrap（带缓存）。</summary>
    private IDalamudTextureWrap? GetOrLoadGameIcon(uint iconId)
    {
        if (iconTexture == null || lastIconId != iconId)
        {
            iconTexture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId, false, false));
            lastIconId = iconId;
        }

        if (iconTexture != null && iconTexture.TryGetWrap(out var wrap, out _))
        {
            return wrap;
        }

        return null;
    }

    /// <summary>查找当前打开（可见）的背包 addon，返回其指针（只读查询，安全）。</summary>
    /// <remarks>
    /// 背包 addon 在 FFXIV 中可能同时存在多个实例，<c>GetAddonByName(name, index)</c> 的 index
    /// 只是「第几个同名实例」。只锚定 <c>IsVisible</c> 为 true 的活动实例；找不到可见实例时返回
    /// false，使按钮在背包关闭时不显示。
    /// </remarks>
    private static unsafe bool TryGetInventoryAddon(out AtkUnitBase* addon)
    {
        addon = null;

        foreach (var name in CandidateAddonNames)
        {
            for (var i = 0; i < 4; i++)
            {
                var ptr = Plugin.GameGui.GetAddonByName(name, i);
                if (ptr.IsNull)
                {
                    continue;
                }

                var a = (AtkUnitBase*)ptr.Address;
                if (a->IsVisible)
                {
                    addon = a;
                    return true;
                }
            }
        }

        return false;
    }
}
