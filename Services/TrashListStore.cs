using System;
using System.Collections.Generic;
using System.Linq;
using AutoTrash.Core;
using AutoTrash.Models;

namespace AutoTrash.Services;

/// <summary>
/// 待丢弃列表的增删查 / 搜索，底层使用 Configuration.TrashList，并在变更后持久化。
/// </summary>
public class TrashListStore
{
    private readonly Configuration config;

    public TrashListStore(Configuration config)
    {
        this.config = config;
    }

    /// <summary>当前列表（只读视图）。</summary>
    public IReadOnlyList<TrashItemEntry> Items => config.TrashList;

    /// <summary>添加条目（按 ItemId 或 DisplayName 去重）。</summary>
    public void Add(TrashItemEntry entry)
    {
        if (entry == null)
        {
            return;
        }

        // 相同 ItemId 不重复添加
        if (entry.ItemId != 0 && config.TrashList.Any(e => e.ItemId == entry.ItemId))
        {
            return;
        }

        // 相同名称不重复添加
        if (!string.IsNullOrWhiteSpace(entry.DisplayName) &&
            config.TrashList.Any(e => string.Equals(e.DisplayName, entry.DisplayName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        config.TrashList.Add(entry);
        config.Save();
    }

    /// <summary>按条目移除（匹配 ItemId 或 DisplayName）。</summary>
    public void Remove(TrashItemEntry entry)
    {
        if (entry == null)
        {
            return;
        }

        config.TrashList.RemoveAll(e =>
            (e.ItemId != 0 && e.ItemId == entry.ItemId) ||
            (!string.IsNullOrWhiteSpace(entry.DisplayName) &&
             string.Equals(e.DisplayName, entry.DisplayName, StringComparison.OrdinalIgnoreCase)));
        config.Save();
    }

    /// <summary>按下标移除。</summary>
    public void RemoveAt(int index)
    {
        if (index >= 0 && index < config.TrashList.Count)
        {
            config.TrashList.RemoveAt(index);
            config.Save();
        }
    }

    /// <summary>清空列表。</summary>
    public void Clear()
    {
        config.TrashList.Clear();
        config.Save();
    }

    /// <summary>整体替换列表（用于导入）。</summary>
    public void ReplaceAll(List<TrashItemEntry> entries)
    {
        config.TrashList = entries ?? new List<TrashItemEntry>();
        config.Save();
    }

    /// <summary>
    /// 判断物品是否命中丢弃列表。
    /// </summary>
    /// <param name="itemId">来自游戏的 ItemId。</param>
    /// <param name="itemName">来自游戏的展示名称（模糊匹配时使用，可为空）。</param>
    public bool Contains(uint itemId, string? itemName)
    {
        foreach (var e in config.TrashList)
        {
            if (e.IsFuzzy)
            {
                if (!string.IsNullOrWhiteSpace(e.DisplayName) && !string.IsNullOrWhiteSpace(itemName) &&
                    itemName.IndexOf(e.DisplayName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            else
            {
                if (e.ItemId != 0 && e.ItemId == itemId)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 反查条目：优先按精确 ItemId 匹配（仅非模糊条目参与），其次按名称子串回退（模糊条目）。
    /// 与 <see cref="Contains"/> 同匹配优先级。找不到返回 null。
    /// 供 AutoDiscardService 在 Process 时反查条目级阈值使用。
    /// </summary>
    /// <param name="itemId">来自游戏的 ItemId。</param>
    /// <param name="itemName">来自游戏的展示名称（模糊匹配时使用，可为空）。</param>
    public TrashItemEntry? GetEntry(uint itemId, string? itemName)
    {
        // 1) 精确 ItemId 优先（仅非模糊条目）
        foreach (var e in config.TrashList)
        {
            if (!e.IsFuzzy && e.ItemId != 0 && e.ItemId == itemId)
            {
                return e;
            }
        }

        // 2) 模糊子串回退（精确未命中时）
        if (!string.IsNullOrWhiteSpace(itemName))
        {
            foreach (var e in config.TrashList)
            {
                if (e.IsFuzzy && !string.IsNullOrWhiteSpace(e.DisplayName) &&
                    itemName.IndexOf(e.DisplayName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return e;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 原地更新匹配条目的可变字段（展示名 / 模糊标志 / 阈值）后持久化。
    /// 按 ItemId 或 DisplayName 定位（与添加去重一致）。找不到匹配项则无操作。
    /// </summary>
    public void Update(TrashItemEntry e)
    {
        if (e == null)
        {
            return;
        }

        for (var i = 0; i < config.TrashList.Count; i++)
        {
            var cur = config.TrashList[i];
            if ((e.ItemId != 0 && cur.ItemId == e.ItemId) ||
                (!string.IsNullOrWhiteSpace(e.DisplayName) && !string.IsNullOrWhiteSpace(cur.DisplayName) &&
                 string.Equals(cur.DisplayName, e.DisplayName, StringComparison.OrdinalIgnoreCase)))
            {
                cur.DisplayName = e.DisplayName;
                cur.IsFuzzy = e.IsFuzzy;
                cur.QuantityThreshold = e.QuantityThreshold;
                cur.HasThreshold = e.HasThreshold;
                config.TrashList[i] = cur; // 类引用本就同一对象，写回以保证一致
                config.Save();
                return;
            }
        }
    }

    /// <summary>
    /// 设置条目「保留数量」阈值：存在则原地更新 HasThreshold=true / QuantityThreshold；
    /// 不存在则以阈值新增（保留数量默认不丢——初值由调用方决定）。
    /// </summary>
    public void UpdateThreshold(uint itemId, string? itemName, int threshold)
    {
        var existing = GetEntry(itemId, itemName);
        if (existing != null)
        {
            existing.HasThreshold = true;
            existing.QuantityThreshold = threshold;
            config.Save();
        }
        else
        {
            Add(new TrashItemEntry(itemId, itemName ?? string.Empty, false)
            {
                HasThreshold = true,
                QuantityThreshold = threshold,
            });
        }
    }

    /// <summary>
    /// 清除条目阈值：保留在列表中，仅 HasThreshold=false（回退全局策略）。不存在则无操作。
    /// </summary>
    public void ClearThreshold(uint itemId, string? itemName)
    {
        var existing = GetEntry(itemId, itemName);
        if (existing != null)
        {
            existing.HasThreshold = false;
            config.Save();
        }
    }

    /// <summary>持久化。</summary>
    public void Save()
    {
        config.Save();
    }
}
