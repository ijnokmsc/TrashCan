using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Lumina.Excel.Sheets;

namespace AutoTrash.Core;

/// <summary>
/// 物品名称 ↔ ItemId 解析（基于 Lumina 的 Item 表）。
/// 结果带 5 分钟缓存，且任何异常都会安全降级（返回 0 / ItemId 字符串）。
/// </summary>
public class ItemResolver
{
    private readonly IDataManager dataManager;
    private Dictionary<uint, string>? cache;
    private DateTime cacheTime = DateTime.MinValue;

    public ItemResolver(IDataManager dataManager)
    {
        this.dataManager = dataManager;
    }

    private bool EnsureCache()
    {
        if (cache != null && (DateTime.Now - cacheTime).TotalMinutes < 5)
        {
            return true;
        }

        try
        {
            // 通过 Lumina 的 GameData 获取 Item 表
            var sheet = dataManager.GameData.GetExcelSheet<Item>(null, "Item");
            if (sheet == null)
            {
                return false;
            }

            var dict = new Dictionary<uint, string>();
            foreach (var row in sheet)
            {
                if (row.RowId == 0)
                {
                    continue;
                }

                var name = row.Name.ExtractText() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                dict[(uint)row.RowId] = name;
            }

            cache = dict;
            cacheTime = DateTime.Now;
            return true;
        }
        catch
        {
            // 游戏数据未就绪或解析失败：安全降级
            return false;
        }
    }

    /// <summary>
    /// 模糊匹配名称到 ItemId。优先精确匹配（忽略大小写），其次子串匹配。
    /// </summary>
    /// <returns>匹配到的 ItemId；未找到返回 0。</returns>
    public uint ResolveName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return 0;
        }

        var exact = name.Trim();
        var key = exact.ToLowerInvariant();

        if (!EnsureCache() || cache == null)
        {
            return 0;
        }

        // 精确优先
        foreach (var kv in cache)
        {
            if (string.Equals(kv.Value, exact, StringComparison.OrdinalIgnoreCase))
            {
                return kv.Key;
            }
        }

        // 子串模糊
        foreach (var kv in cache)
        {
            if (kv.Value.ToLowerInvariant().Contains(key))
            {
                return kv.Key;
            }
        }

        return 0;
    }

    /// <summary>
    /// 根据 ItemId 获取展示名称。解析优先级：
    /// 1. 内存缓存（EnsureCache，已用 ExtractText 规范化）；
    /// 2. 缓存 miss 时直接查 Item 表取可读文本；
    /// 3. 仍取不到时回退查 EventItem 表（关键道具 / 事件物品）；
    /// 4. 全部失败则安全降级为 ItemId 字符串。
    /// </summary>
    public string GetName(uint itemId)
    {
        if (itemId == 0)
        {
            return string.Empty;
        }

        if (EnsureCache() && cache != null && cache.TryGetValue(itemId, out var name))
        {
            return name;
        }

        // 缓存 miss：直接查 Item 表，避免一次性大批量 miss 时回退成纯数字
        string? DirectItemName(uint id)
        {
            try
            {
                var sheet = dataManager?.GameData.GetExcelSheet<Item>(null, "Item");
                if (sheet == null)
                {
                    return null;
                }

                var direct = sheet.GetRow(id).Name.ExtractText();
                return string.IsNullOrWhiteSpace(direct) ? null : direct;
            }
            catch
            {
                // Item 表回退失败，继续 EventItem 回退
                return null;
            }
        }

        var directName = DirectItemName(itemId);
        if (directName != null)
        {
            return directName;
        }

        // HQ 偏移回退：ItemId = baseId + 1_000_000
        if (itemId > 1_000_000)
        {
            directName = DirectItemName(itemId - 1_000_000);
            if (directName != null)
            {
                return directName + " [HQ]";
            }
        }

        // EventItem 表 fallback（关键道具 / 事件物品）
        var eventName = GetEventItemName(itemId);
        if (!string.IsNullOrWhiteSpace(eventName))
        {
            return eventName;
        }

        // 游戏物品链接回退（仅游戏运行时启用；dataManager 为 null 的离线测试环境跳过，避免 SeStringBuilder 阻塞）
        if (dataManager != null)
        {
            var linkName = GetItemLinkName(itemId, false);
            if (!string.IsNullOrWhiteSpace(linkName) && linkName != itemId.ToString())
                return linkName;
        }

        return itemId.ToString();
    }

    /// <summary>
    /// 从 EventItem 表解析名称（关键道具 / 事件物品）。取不到或异常时安全返回 null。
    /// </summary>
    /// <param name="itemId">物品 ItemId。</param>
    /// <returns>可读名称；取不到返回 null。</returns>
    private string? GetEventItemName(uint itemId)
    {
        try
        {
            var sheet = dataManager?.GameData.GetExcelSheet<EventItem>(null, "EventItem");
            if (sheet == null)
            {
                return null;
            }

            var name = sheet.GetRow(itemId).Name.ExtractText();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>用游戏物品链接回退取名称（覆盖 Lumina 表缺失/未加载的特殊情况）。</summary>
    private string? GetItemLinkName(uint itemId, bool isHq)
    {
        try
        {
            var seString = new SeStringBuilder().AddItemLink(itemId, isHq).Build();
            if (seString?.Payloads == null)
            {
                return null;
            }

            // 仅拼接纯文本负载（排除颜色/链接等控制码），得到可读物品名。
            // 注：Dalamud 的 SeString 没有 Lumina 的 ExtractText 扩展，故手动遍历 TextPayload。
            var sb = new System.Text.StringBuilder();
            foreach (var payload in seString.Payloads)
            {
                if (payload is TextPayload textPayload)
                {
                    sb.Append(textPayload.Text);
                }
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 判定指定 ItemId 是否为装备。Lumina 规则：<c>EquipSlotCategory.RowId != 0</c> 即装备。
    /// 安全降级：itemId 为 0 / dataManager 为 null / 取不到行（返回 default(Item)，其 RowId 为 0）/ 任何异常 → 返回 false（无法判定就不拦）。
    /// 标记为 virtual 便于单元测试通过派生类 override 离线验证。
    /// </summary>
    /// <param name="itemId">物品 ItemId。</param>
    /// <returns>是装备返回 true，否则返回 false。</returns>
    public virtual bool IsEquipItem(uint itemId)
    {
        if (itemId == 0)
        {
            return false;
        }

        try
        {
            // dataManager 为 null 时整体表达式为 null，sheet 判空后安全降级
            var sheet = dataManager?.GameData.GetExcelSheet<Item>(null, "Item");
            if (sheet == null)
            {
                return false;
            }

            // 经 Hooks/dev 程序集反射确认：GetRow 返回非空的 Item 结构体。
            // 行不存在时返回 default(Item)，其 EquipSlotCategory.RowId 为 0，自然降级为 false。
            var row = sheet.GetRow(itemId);
            return row.EquipSlotCategory.RowId != 0;
        }
        catch
        {
            // 游戏数据未就绪或解析失败：安全降级，不拦截
            return false;
        }
    }

    /// <summary>
    /// 获取物品图标 ID（Lumina Item.Icon，ushort 类型）。无法解析时返回 0。
    /// 安全降级：itemId 为 0 / dataManager 为 null / 取不到行 / 任何异常 → 返回 0。
    /// 标记为 virtual 便于单元测试通过派生类 override 离线验证。
    /// </summary>
    /// <param name="itemId">物品 ItemId。</param>
    /// <returns>图标 ID（ushort）；失败返回 0。</returns>
    public virtual ushort GetIconId(uint itemId)
    {
        if (itemId == 0)
        {
            return 0;
        }

        ushort GetRowIcon(uint id)
        {
            try
            {
                var sheet = dataManager?.GameData.GetExcelSheet<Item>(null, "Item");
                if (sheet == null)
                {
                    return 0;
                }

                return sheet.GetRow(id).Icon; // ushort
            }
            catch
            {
                // 游戏数据未就绪或解析失败：安全降级
                return 0;
            }
        }

        var icon = GetRowIcon(itemId);
        if (icon != 0)
        {
            return icon;
        }

        // HQ 物品常见编码：ItemId = baseId + 1_000_000
        if (itemId > 1_000_000)
        {
            icon = GetRowIcon(itemId - 1_000_000);
            if (icon != 0)
            {
                return icon;
            }
        }

        return 0;
    }
}
