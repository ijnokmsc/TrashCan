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

    /// <summary>持久化。</summary>
    public void Save()
    {
        config.Save();
    }
}
