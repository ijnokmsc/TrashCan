namespace AutoTrash.Models;

/// <summary>
/// 待丢弃条目。
/// 匹配规则：
/// - IsFuzzy == false 且 ItemId != 0：按精确 ItemId 匹配；
/// - IsFuzzy == true：按 DisplayName 在物品名称中的子串（不区分大小写）模糊匹配。
/// </summary>
public class TrashItemEntry
{
    /// <summary>精确匹配用的 ItemId；为 0 时仅按名称模糊匹配。</summary>
    public uint ItemId { get; set; }

    /// <summary>展示名称（精确匹配时作为备注；模糊匹配时作为关键字）。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>是否启用模糊（子串）匹配。</summary>
    public bool IsFuzzy { get; set; }

    public TrashItemEntry()
    {
    }

    public TrashItemEntry(uint itemId, string displayName, bool isFuzzy = false)
    {
        ItemId = itemId;
        DisplayName = displayName;
        IsFuzzy = isFuzzy;
    }
}
