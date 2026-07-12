using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Inventory;
using FFXIVClientStructs.FFXIV.Client.Game;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Windowing;
using AutoTrash.Core;
using AutoTrash.Models;
using AutoTrash.Services;

namespace AutoTrash.Windows;

/// <summary>
/// ImGui 主窗口：添加区 / 列表区 / 设置区 / 日志区 四个 Tab。
/// </summary>
public class MainWindow : Window
{
    private readonly Plugin plugin;
    private readonly ItemResolver resolver;
    private readonly TrashListStore listStore;
    private readonly LogStore logStore;
    private readonly Configuration config;

    private string searchInput = string.Empty;
    private string ioBuffer = string.Empty;
    private string statusMsg = string.Empty;

    // 「清空列表」确认弹窗开关：点击按钮时置 true 并 OpenPopup 一次，避免每帧重复调用 OpenPopup。
    private bool clearListConfirmOpen = false;

    // 「首次使用警告」弹窗开关：第一次打开主窗口时置 true 并 OpenPopup 一次，避免每帧重复调用 OpenPopup。
    private bool firstWarnOpen = false;

    // 物品图标纹理缓存：key = Lumina 图标 ID，缓存共享纹理句柄（同步可取，避免缓存尚未加载完成的 null wrap）
    private readonly Dictionary<ushort, ISharedImmediateTexture?> itemIconCache = new();

    /// <summary>背包网格展示模式参数：格子尺寸 / 图标尺寸 / 间距 / 最小列数。</summary>
    private readonly record struct GridModeSpec(float CellSize, float IconSize, float Spacing, int MinColumns);

    /// <summary>三种展示模式的参数表：Compact（紧凑）/ Standard（标准）/ Relaxed（宽松）。</summary>
    private static readonly Dictionary<string, GridModeSpec> GridModes = new()
    {
        ["Compact"] = new GridModeSpec(40f, 26f, 2f, 5),
        ["Standard"] = new GridModeSpec(50f, 36f, 4f, 5),
        ["Relaxed"] = new GridModeSpec(60f, 44f, 6f, 5),
    };

    /// <summary>读取当前配置选定的网格模式参数；未识别时回退到 Standard。</summary>
    private GridModeSpec CurrentGridMode
    {
        get
        {
            if (GridModes.TryGetValue(config.GridMode, out var spec))
            {
                return spec;
            }

            return GridModes["Standard"];
        }
    }

    /// <summary>通用背包格子数据：供 DrawItemGrid 复用的单个物品格子信息。</summary>
    private readonly record struct GridCell(
        uint ItemId,
        int Quantity,
        bool IsSelected,
        string Tooltip,
        bool ShowQuantity);

    public MainWindow(Plugin plugin) : base($"AutoTrash 自动丢弃垃圾桶 {GetVersionString()}")
    {
        this.plugin = plugin;
        this.resolver = plugin.ItemResolver;
        this.listStore = plugin.TrashListStore;
        this.logStore = plugin.LogStore;
        this.config = plugin.Configuration;
        Size = new Vector2(520, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    /// <summary>窗口打开时由基类调用：暂停自动丢弃，防止用户在编辑/添加列表物品期间被立即删除。</summary>
    public override void OnOpen()
    {
        // 打开插件时暂停自动删除，防止编辑/添加列表期间误丢
        plugin.AutoDiscardService.Paused = true;
        base.OnOpen();
    }

    /// <summary>窗口关闭时由基类调用：恢复自动丢弃并执行一次扫描删除。
    /// 注：Dalamud 的 WindowSystem 只对打开的窗口调用 Draw()，真正关闭后 Draw 不再被调用，
    /// 因此关闭时的恢复逻辑必须放在 OnClose() 而非 Draw() 内。</summary>
    public override void OnClose()
    {
        // 关闭窗口后恢复自动删除，并执行一次扫描删除
        plugin.AutoDiscardService.Paused = false;
        TriggerScanOnClose();
        base.OnClose();
    }

    /// <summary>读取插件程序集版本，格式 v{Major}.{Minor}.{Build}（参考 CraftFlow 约定）。</summary>
    private static string GetVersionString()
    {
        try
        {
            var ver = typeof(AutoTrash.Plugin).Assembly.GetName().Version;
            return $"v{ver?.Major}.{ver?.Minor}.{ver?.Build}";
        }
        catch
        {
            return "v?.?.?";
        }
    }

    /// <summary>获取物品图标共享纹理句柄（带缓存）。返回 ISharedImmediateTexture（同步可取，不会为 null），由调用方按需取已加载纹理。ItemId 无效或图标不可用返回 null。</summary>
    private ISharedImmediateTexture? GetItemIcon(uint itemId)
    {
        if (itemId == 0)
        {
            return null;
        }

        var iconId = resolver.GetIconId(itemId);
        if (iconId == 0)
        {
            return null;
        }

        if (itemIconCache.TryGetValue(iconId, out var cached))
        {
            return cached;
        }

        // 缓存共享纹理句柄本身（而非 GetWrapOrDefault 返回的可能为 null 的 wrap），
        // 句柄同步即可拿到，之后由其异步按需加载纹理，避免缓存到首帧未加载完成的 null。
        var shared = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId, false, false));
        itemIconCache[iconId] = shared;
        return shared;
    }

    /// <summary>在一行起始处绘制物品图标；无图标或纹理尚未就绪时绘制等宽占位，保持对齐。默认 22x22，用于紧凑列表。</summary>
    private void DrawItemIcon(uint itemId, float size = 22)
    {
        var shared = GetItemIcon(itemId);
        if (shared != null && shared.TryGetWrap(out var wrap, out _) && wrap != null && wrap.Handle != IntPtr.Zero)
        {
            ImGui.Image(wrap.Handle, new Vector2(size, size));
        }
        else
        {
            ImGui.Dummy(new Vector2(size, size));
        }
    }

    /// <summary>取条目的展示名：优先用 DisplayName，若为空或等于 ItemId 字符串则用 ItemResolver 重新解析（解决当初加列表时数据未就绪只存了 ItemId 的情况）。</summary>
    private string GetEntryDisplayName(TrashItemEntry e)
    {
        if (!string.IsNullOrWhiteSpace(e.DisplayName) && e.DisplayName != e.ItemId.ToString())
            return e.DisplayName;
        var resolved = resolver.GetName(e.ItemId);
        return resolved != e.ItemId.ToString() ? resolved : $"物品ID {e.ItemId}";
    }

    /// <summary>通用背包网格模式选择器：紧凑 / 标准 / 宽松（持久化到 Configuration.GridMode）。添加页与列表页共用。</summary>
    private void DrawGridModeSelector()
    {
        var modeNames = new[] { "Compact", "Standard", "Relaxed" };
        for (var mi = 0; mi < modeNames.Length; mi++)
        {
            var m = modeNames[mi];
            var selected = config.GridMode == m;
            if (ImGui.RadioButton(m, selected))
            {
                config.GridMode = m;
                config.Save();
            }

            if (mi < modeNames.Length - 1)
            {
                ImGui.SameLine();
            }
        }
    }

    /// <summary>
    /// 通用背包网格绘制：接收一个已解析的格子列表与一个点击回调（按格子下标触发），
    /// 绘制仿游戏背包网格（背景 / 边框 / 图标 / 数量角标 / 整格 InvisibleButton 点击 / 悬停 tooltip）。
    /// 添加页（背包物品，点击切换列表）与列表页（已加入物品，点击移除）共用本方法。
    /// </summary>
    /// <param name="cells">格子数据（顺序即展示顺序，点击回调的 index 与之对应）。</param>
    /// <param name="onCellClick">整格被点击时的回调，传入被点击格子的下标。</param>
    private void DrawItemGrid(IReadOnlyList<GridCell> cells, Action<int> onCellClick)
    {
        if (cells.Count == 0)
        {
            return;
        }

        var mode = CurrentGridMode;
        var cellSize = mode.CellSize;
        var iconSize = mode.IconSize;
        var spacing = mode.Spacing;
        var availWidth = ImGui.GetContentRegionAvail().X;
        var columns = Math.Max(1, (int)((availWidth + spacing) / (cellSize + spacing)));

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(spacing, spacing));
        var drawList = ImGui.GetWindowDrawList();

        // 主题配色（Dalamud 默认深色半透明 UI，本就接近游戏原生背包）
        var themeFrameBg = ImGui.GetColorU32(ImGuiCol.FrameBg);
        var themeFrameBgHovered = ImGui.GetColorU32(ImGuiCol.FrameBgHovered);
        var themeBorder = ImGui.GetColorU32(ImGuiCol.Border);
        // 选中态金色：不透明描边 0xFF4AA0C8u（RGB=0xC8,0xA0,0x4A），半透明填充 0x332EA04Au（A=0x33）
        const uint goldBorder = 0xFF4AA0C8u;
        const uint goldFill = 0x332EA04Au;

        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            var topLeft = ImGui.GetCursorScreenPos();
            var bottomRight = topLeft + new Vector2(cellSize, cellSize);

            // 悬停预判（不依赖控件，可在画背景前安全调用）
            var hovered = ImGui.IsMouseHoveringRect(topLeft, bottomRight);

            // 背景：选中=半透明金，悬停=主题悬停色，普通=主题底（圆角 4 更像游戏格子）
            var bg = cell.IsSelected ? goldFill : (hovered ? themeFrameBgHovered : themeFrameBg);
            drawList.AddRectFilled(topLeft, bottomRight, bg, 4f);

            // 边框：选中=金色，否则=主题 Border（暗色）
            var border = cell.IsSelected ? goldBorder : themeBorder;
            drawList.AddRect(topLeft, bottomRight, border, 4f, ImDrawFlags.None, 1.5f);

            // 图标（居中偏上，游戏原生图标，保持 HQ 偏移修复）
            ImGui.SetCursorScreenPos(topLeft + new Vector2((cellSize - iconSize) / 2, 2));
            DrawItemIcon(cell.ItemId, iconSize);

            // 数量角标（右下角，白字 + 黑描边，模拟游戏风格）
            if (cell.ShowQuantity && cell.Quantity > 1)
            {
                var qtyText = $"x{cell.Quantity}";
                var textSize = ImGui.CalcTextSize(qtyText);
                var qtyPos = new Vector2(bottomRight.X - textSize.X - 3, bottomRight.Y - ImGui.GetFontSize() - 2);
                // 黑色描边：四向偏移各画一遍
                drawList.AddText(qtyPos + new Vector2(1, 0), 0xFF000000u, qtyText);
                drawList.AddText(qtyPos + new Vector2(-1, 0), 0xFF000000u, qtyText);
                drawList.AddText(qtyPos + new Vector2(0, 1), 0xFF000000u, qtyText);
                drawList.AddText(qtyPos + new Vector2(0, -1), 0xFF000000u, qtyText);
                // 白色字面
                drawList.AddText(qtyPos, 0xFFFFFFFFu, qtyText);
            }

            // 选中标记：右上角金色对勾（替代原 Checkbox 控件）
            if (cell.IsSelected)
            {
                drawList.AddText(topLeft + new Vector2(3, 1), goldBorder, "✓");
            }

            // 整格点击交互：覆盖整格的 InvisibleButton，点击即触发回调
            ImGui.SetCursorScreenPos(topLeft);
            ImGui.PushID($"grid_cell_{cell.ItemId}_{i}");
            if (ImGui.InvisibleButton($"##cell{cell.ItemId}_{i}", new Vector2(cellSize, cellSize)))
            {
                onCellClick(i);
            }

            ImGui.PopID();

            // 悬停 tooltip（基于 InvisibleButton 的 hover 态）
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(cell.Tooltip);
            }

            // 非行尾则同行排列（InvisibleButton 已占 cellSize 布局空间，无需 Dummy）
            if ((i + 1) % columns != 0)
            {
                ImGui.SameLine();
            }
        }

        ImGui.PopStyleVar();
    }

    public override void Draw()
    {
        ImGui.SetNextWindowSize(new Vector2(520, 520), ImGuiCond.FirstUseEver);
        var open = IsOpen;
        if (!ImGui.Begin(WindowName, ref open))
        {
            // 关闭逻辑已移至 OnClose()（基类在窗口真正关闭时调用，Draw 不再被调用）。
            // 此处仅在 Begin 失败时不绘制 UI。
            ImGui.End();
            return;
        }

        IsOpen = open;

        // 首次使用警告：仅第一次打开主窗口时弹出一次（用配置持久化标记）。
        if (!config.HasShownWarning)
        {
            firstWarnOpen = true;
            config.HasShownWarning = true;
            config.Save();
            ImGui.OpenPopup("首次使用警告");
        }

        // 首次使用警告模态弹窗：说明物品删除后无法找回、请谨慎添加。
        if (firstWarnOpen && ImGui.BeginPopupModal("首次使用警告", ref firstWarnOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextWrapped("⚠ 警告：本插件会自动丢弃您列表中的物品。\n物品一旦删除将无法找回，请务必谨慎添加物品。");
            ImGui.Separator();
            if (ImGui.Button("我已知晓风险", new Vector2(200, 0)))
            {
                firstWarnOpen = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        ImGui.BeginTabBar("mainTabs");
        if (ImGui.BeginTabItem("添加"))
        {
            DrawAddTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("列表"))
        {
            DrawListTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("设置"))
        {
            DrawSettingsTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("日志"))
        {
            DrawLogTab();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();

        UiHelpers.Status(statusMsg);
        statusMsg = string.Empty;

        ImGui.End();
    }

    /// <summary>
    /// 窗口关闭回调：在关闭时执行一次扫描。
    /// 仅调用 InventoryWatcher.TriggerScan()；若 pendingItems 为空则内部自然返回，不触发任何丢弃。
    /// 调用前确保 plugin 与 plugin.InventoryWatcher 非空。
    /// </summary>
    private void TriggerScanOnClose()
    {
        if (plugin != null && plugin.InventoryWatcher != null)
        {
            plugin.InventoryWatcher.TriggerScan();
        }
    }

    /// <summary>
    /// “添加”页：仿游戏背包网格视图（Inventory1-4，勾选即加入 / 取消即移除丢弃列表）。
    /// </summary>
    private void DrawAddTab()
    {
        DrawInventoryGridSection();
    }

    /// <summary>右列：仿游戏背包网格视图，仅展示 Inventory1-4 物品，勾选即加入 / 取消即移除丢弃列表。</summary>
    private void DrawInventoryGridSection()
    {
        ImGui.Text("仿游戏背包：点击格子加入 / 移除丢弃列表");
        UiHelpers.Hint("点击格子切换：选中即加入丢弃列表，取消即移除。");

        // 模式切换：紧凑 / 标准 / 宽松（持久化到 Configuration.GridMode）
        DrawGridModeSelector();
        ImGui.Spacing();

        var bagItems = ReadBackpackItems();
        if (bagItems.Count == 0)
        {
            UiHelpers.Hint("背包为空，或尚未登录 / 游戏数据未就绪。");
            return;
        }

        // 构造通用格子数据，交给 DrawItemGrid 绘制
        var cells = new List<GridCell>(bagItems.Count);
        for (var i = 0; i < bagItems.Count; i++)
        {
            var bag = bagItems[i];
            var listed = listStore.Items.Any(e => e.ItemId == bag.ItemId);
            cells.Add(new GridCell(
                bag.ItemId,
                bag.Quantity,
                listed,
                $"{GetBagItemDisplayName(bag)}\nItemId={bag.ItemId}{(bag.IsHq ? " [HQ]" : string.Empty)}",
                true));
        }

        DrawItemGrid(cells, i => ToggleTrashItem(bagItems[i], !cells[i].IsSelected));
    }

    /// <summary>实时读取背包（Inventory1-4）物品；主线程绘制期调用，安全，不缓存到字段以避免 stale。</summary>
    private List<(uint ItemId, string Name, bool IsHq, int Quantity)> ReadBackpackItems()
    {
        var bagItems = new List<(uint, string, bool, int)>();
        var backpackOnly = new[]
        {
            InventoryType.Inventory1,
            InventoryType.Inventory2,
            InventoryType.Inventory3,
            InventoryType.Inventory4,
        };

        foreach (var ct in backpackOnly)
        {
            try
            {
                var items = Plugin.GameInventory.GetInventoryItems((GameInventoryType)(int)ct);
                foreach (var it in items)
                {
                    if (it.ItemId == 0 || it.IsEmpty) continue;
                    bagItems.Add((it.ItemId, resolver.GetName(it.ItemId), it.IsHq, it.Quantity));
                }
            }
            catch
            {
                // 容器不可访问忽略
            }
        }

        return bagItems;
    }

    /// <summary>取背包物品的展示名（解决数据未就绪时只拿到 ItemId 的情况）。</summary>
    private string GetBagItemDisplayName((uint ItemId, string Name, bool IsHq, int Quantity) bag)
    {
        if (!string.IsNullOrWhiteSpace(bag.Name) && bag.Name != bag.ItemId.ToString())
        {
            return bag.Name;
        }

        var resolved = resolver.GetName(bag.ItemId);
        return resolved != bag.ItemId.ToString() ? resolved : $"物品ID {bag.ItemId}";
    }

    /// <summary>勾选切换：加入 / 移除丢弃列表（按 ItemId 匹配）。</summary>
    private void ToggleTrashItem((uint ItemId, string Name, bool IsHq, int Quantity) bag, bool addToList)
    {
        if (addToList)
        {
            if (!listStore.Items.Any(e => e.ItemId == bag.ItemId))
            {
                listStore.Add(new TrashItemEntry(bag.ItemId, GetBagItemDisplayName(bag), false));
                statusMsg = $"已添加：{GetBagItemDisplayName(bag)}";
            }
        }
        else
        {
            var existing = listStore.Items.FirstOrDefault(e => e.ItemId == bag.ItemId);
            if (existing != null)
            {
                listStore.Remove(existing);
                statusMsg = $"已移除：{GetBagItemDisplayName(bag)}";
            }
        }
    }

    private void DrawListTab()
    {
        // 手动 / 定时扫描控制：醒目按钮，立即触发一次扫描丢弃
        if (ImGui.Button("立即扫描（手动检测待丢弃物品）", new Vector2(-1, 0)))
        {
            plugin.InventoryWatcher.TriggerScan();
            statusMsg = "已触发一次手动扫描。";
        }

        UiHelpers.HelpMarker("自动丢弃改为手动/定时扫描：物品获得事件仅记录，到扫描时刻才执行丢弃。此按钮可随时手动触发一次（即使关闭了定时扫描也可用）。");
        ImGui.Separator();

        ImGui.InputText("搜索", ref searchInput, 128);

        ImGui.SameLine();
        if (ImGui.Button("清空列表"))
        {
            // 打开确认弹窗（仅点击时触发一次，避免每帧重复调用 OpenPopup）
            clearListConfirmOpen = true;
            ImGui.OpenPopup("确认清空列表");
        }

        ImGui.Separator();

        // 「清空列表」确认弹窗（模态）：确定则清空并关闭，取消仅关闭。
        if (ImGui.BeginPopupModal("确认清空列表", ref clearListConfirmOpen, ImGuiWindowFlags.None))
        {
            ImGui.Text("确定要清空丢弃列表吗？此操作不可撤销。");
            if (ImGui.Button("确定", new Vector2(120, 0)))
            {
                listStore.Clear();
                config.Save();
                statusMsg = "列表已清空。";
                clearListConfirmOpen = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("取消", new Vector2(120, 0)))
            {
                clearListConfirmOpen = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        // 仿游戏背包网格：展示已加入丢弃列表的物品，点击整格即移除该物品。
        DrawListGrid();

        ImGui.Separator();
        ImGui.Text("导入 / 导出：");

        if (ImGui.Button("导出 JSON → 文本框"))
        {
            ioBuffer = ImportExport.ExportJson(new List<TrashItemEntry>(listStore.Items));
            statusMsg = "已导出为 JSON 到下方文本框。";
        }

        ImGui.SameLine();
        if (ImGui.Button("导出 CSV → 文本框"))
        {
            ioBuffer = ImportExport.ExportCsv(new List<TrashItemEntry>(listStore.Items));
            statusMsg = "已导出为 CSV 到下方文本框。";
        }

        ImGui.SameLine();
        if (ImGui.Button("从文本框导入"))
        {
            var imported = ImportExport.ImportAuto(ioBuffer);
            listStore.ReplaceAll(imported);
            statusMsg = $"已从文本框导入 {imported.Count} 条。";
        }

        ImGui.SameLine();
        if (ImGui.Button("导出到文件"))
        {
            try
            {
                var path = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "trash_export.json");
                ImportExport.WriteFile(path, ImportExport.ExportJson(new List<TrashItemEntry>(listStore.Items)));
                statusMsg = $"已写入文件：{path}";
            }
            catch (Exception ex)
            {
                statusMsg = $"导出文件失败：{ex.Message}";
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("从文件导入"))
        {
            try
            {
                var path = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "trash_export.json");
                var text = ImportExport.ReadFile(path);
                var imported = ImportExport.ImportAuto(text);
                listStore.ReplaceAll(imported);
                statusMsg = $"已从文件导入 {imported.Count} 条。";
            }
            catch (Exception ex)
            {
                statusMsg = $"导入文件失败：{ex.Message}";
            }
        }

        ImGui.InputTextMultiline("##io", ref ioBuffer, 8192, new Vector2(-1, 120));
    }

    /// <summary>
    /// 列表页的仿游戏背包网格：展示已加入丢弃列表的物品（可包含不在当前背包中的物品）。
    /// 与添加页共用 DrawItemGrid；支持 Compact/Standard/Relaxed 三种模式（沿用 CurrentGridMode）。
    /// 列表物品无数量信息，故不显示数量角标；点击整格即从列表中移除该物品。
    /// 搜索框在网格之前已输入，此处按名称/ItemId 过滤后只展示匹配项。
    /// </summary>
    private void DrawListGrid()
    {
        DrawGridModeSelector();
        ImGui.Spacing();

        var items = listStore.Items;
        if (items.Count == 0)
        {
            UiHelpers.Hint("列表为空。在“添加”页点击背包物品加入，或用下方导入功能。");
            return;
        }

        // 搜索过滤：按展示名或 ItemId 匹配（保留空搜索时展示全部）
        var filtered = new List<TrashItemEntry>(items.Count);
        foreach (var e in items)
        {
            if (!string.IsNullOrWhiteSpace(searchInput) &&
                !(GetEntryDisplayName(e).Contains(searchInput, StringComparison.OrdinalIgnoreCase) ||
                  e.ItemId.ToString().Contains(searchInput, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            filtered.Add(e);
        }

        if (filtered.Count == 0)
        {
            UiHelpers.Hint("列表为空，或没有匹配搜索的结果。");
            return;
        }

        // 构造通用格子数据：列表物品无数量，ShowQuantity=false（只显示图标 + 名称 tooltip）
        var cells = new List<GridCell>(filtered.Count);
        foreach (var e in filtered)
        {
            var name = GetEntryDisplayName(e);
            cells.Add(new GridCell(
                e.ItemId,
                1,
                false,
                $"{name}\nItemId={e.ItemId}{(e.IsFuzzy ? "  |  模糊匹配" : string.Empty)}",
                false));
        }

        // 点击整格即从列表中移除该物品（filtered 为本次快照，移除不影响已构建的网格）
        DrawItemGrid(cells, i => listStore.Remove(filtered[i]));
    }

    private void DrawSettingsTab()
    {
        // 第 1 行：启用自动丢弃 + 丢弃 HQ 物品
        var enabled = config.Enabled;
        if (ImGui.Checkbox("启用自动丢弃", ref enabled))
        {
            config.Enabled = enabled;
            config.Save();
        }

        ImGui.SameLine();
        var discardHq = config.DiscardHq;
        if (ImGui.Checkbox("丢弃 HQ 物品", ref discardHq))
        {
            config.DiscardHq = discardHq;
            config.Save();
        }

        ImGui.SameLine();
        UiHelpers.HelpMarker("默认不勾选，即保护 HQ 物品不被丢弃。");

        // 第 2 行：保护装备/特殊物品（默认开） + 允许丢弃装备
        var protectSpecial = config.ProtectSpecialItems;
        if (ImGui.Checkbox("保护装备/特殊物品（默认开）", ref protectSpecial))
        {
            config.ProtectSpecialItems = protectSpecial;
            config.Save();
        }

        ImGui.SameLine();
        UiHelpers.HelpMarker("默认勾选。拦截列表中“已脱下的装备”等本会触发二次确认的高风险物品被无声无确认丢弃；取消勾选则不再按物品类型拦截。");

        ImGui.SameLine();
        var allowEquip = config.AllowDiscardEquip;
        if (ImGui.Checkbox("允许丢弃装备", ref allowEquip))
        {
            config.AllowDiscardEquip = allowEquip;
            config.Save();
        }

        ImGui.SameLine();
        UiHelpers.HelpMarker("仅在“保护装备/特殊物品”保持勾选时才有意义：勾选后，列表中的装备将照常按其他规则（HQ/数量/容器）处理，需用户明确允许。");

        ImGui.Separator();
        ImGui.Text("扫描模式（重构）：");

        // 第 3 行：监听物品获得事件（推荐开启） + 仅手动扫描（关闭定时扫描）
        var enableAdded = config.EnableAddedItemDetection;
        if (ImGui.Checkbox("监听物品获得事件（推荐开启）", ref enableAdded))
        {
            config.EnableAddedItemDetection = enableAdded;
            config.Save();
        }

        ImGui.SameLine();
        UiHelpers.HelpMarker("关闭后完全不响应背包变更事件，所有自动检测失效（仅手动扫描有数据时才有效）。");

        ImGui.SameLine();
        var manualOnly = config.ManualScanOnly;
        if (ImGui.Checkbox("仅手动扫描（关闭定时扫描）", ref manualOnly))
        {
            config.ManualScanOnly = manualOnly;
            config.Save();
        }

        ImGui.SameLine();
        UiHelpers.HelpMarker("勾选后，丢弃只在点击“列表”页的“立即扫描”按钮时触发；不勾选则按下方间隔自动定时扫描。");

        // 第 4 行：定时扫描间隔（秒）输入框 + 数量策略下拉框
        var interval = config.ScanIntervalSeconds;
        ImGui.PushItemWidth(120f);
        if (ImGui.InputInt("定时扫描间隔（秒）", ref interval))
        {
            if (interval < 1)
            {
                interval = 1;
            }

            config.ScanIntervalSeconds = interval;
            config.Save();
        }

        ImGui.PopItemWidth();
        ImGui.SameLine();
        var modeIndex = (int)config.Mode;
        var modeItems = new[] { "整堆丢弃", "超阈值整堆丢弃", "保留阈值以下（仅丢超出部分）" };
        ImGui.PushItemWidth(120f);
        if (ImGui.Combo("数量策略", ref modeIndex, modeItems, modeItems.Length))
        {
            config.Mode = (QuantityMode)modeIndex;
            config.Save();
        }

        ImGui.PopItemWidth();

        ImGui.SameLine();
        UiHelpers.HelpMarker("仅在不勾选“仅手动扫描”时生效；最小值为 1 秒。");

        // 第 5 行：数量阈值输入框 + 日志滚动上限输入框
        var threshold = config.QuantityThreshold;
        ImGui.PushItemWidth(120f);
        if (ImGui.InputInt("数量阈值", ref threshold))
        {
            config.QuantityThreshold = threshold;
            config.Save();
        }

        ImGui.PopItemWidth();
        ImGui.SameLine();
        var logCap = config.LogCap;
        ImGui.PushItemWidth(120f);
        if (ImGui.InputInt("日志滚动上限", ref logCap))
        {
            config.LogCap = logCap;
            config.Save();
        }

        ImGui.PopItemWidth();

        ImGui.SameLine();
        UiHelpers.HelpMarker("DiscardAll：忽略阈值，整堆丢弃。DiscardAboveThreshold：数量超过阈值才整堆丢弃。KeepBelowThreshold：仅丢弃超过阈值的部分，保留阈值数量。");

        ImGui.Separator();
        if (ImGui.Button("保存设置"))
        {
            config.Save();
            statusMsg = "设置已保存。";
        }

        ImGui.Separator();
        UiHelpers.HintWrapped("自动丢弃仅作用于白名单容器：背包1~4。陆行鸟鞍囊、部队存储柜、军武库、信箱、关键道具、雇员与房屋等容器均受保护，不会被自动丢弃。");

        ImGui.Separator();
        UiHelpers.HintWrapped("自动丢弃已改为手动/定时扫描模式：物品获得事件仅做记录，到扫描时刻（定时或手动点击“立即扫描”）才执行丢弃。即点“列表”页的“立即扫描”按钮可随时手动触发；勾选“仅手动扫描”后完全依赖该按钮。");
    }

    private void DrawLogTab()
    {
        if (ImGui.Button("清空日志"))
        {
            logStore.Clear();
            statusMsg = "日志已清空。";
        }

        ImGui.Separator();

        ImGui.BeginChild("logScroll", new Vector2(-1, -1), false);
        var entries = logStore.Entries;
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            var e = entries[i];
            var color = e.Success ? new Vector4(0.4f, 1f, 0.5f, 1f) : new Vector4(1f, 0.4f, 0.4f, 1f);
            ImGui.TextColored(color, $"[{e.Time:HH:mm:ss}] {e.ItemName} (ItemId={e.ItemId}) x{e.Quantity} @容器{e.Container} : {(e.Success ? "成功" : "失败")} - {e.Note}");
        }

        if (entries.Count == 0)
        {
            UiHelpers.Hint("暂无丢弃记录。");
        }

        ImGui.EndChild();
    }
}
