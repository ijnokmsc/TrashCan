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

    /// <summary>
    /// 条目级「保留数量」阈值。当 <see cref="HasThreshold"/> 为 true 时，以本值作为 KeepBelowThreshold 的阈值，
    /// 仅丢弃超出本值的部分（保留本值个）。默认 0。
    /// 旧配置反序列化时缺省为 0，不影响既有「无阈值=全局策略」行为（零回归）。
    /// </summary>
    public int QuantityThreshold { get; set; } = 0;

    /// <summary>
    /// 是否启用条目级阈值。true 时强制 KeepBelowThreshold + <see cref="QuantityThreshold"/>；
    /// false（默认）时回退全局 config.Mode / config.QuantityThreshold。
    /// 旧配置反序列化缺省为 false，零回归。
    /// </summary>
    public bool HasThreshold { get; set; } = false;

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
