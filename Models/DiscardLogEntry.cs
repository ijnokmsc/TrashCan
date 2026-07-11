using System;

namespace AutoTrash.Models;

/// <summary>
/// 单次丢弃操作的日志记录条目。
/// </summary>
public class DiscardLogEntry
{
    /// <summary>丢弃发生时间。</summary>
    public DateTime Time { get; set; }

    /// <summary>物品 ItemId。</summary>
    public uint ItemId { get; set; }

    /// <summary>物品名称（解析失败时回退为 ItemId 字符串）。</summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>尝试丢弃的数量。</summary>
    public uint Quantity { get; set; }

    /// <summary>所在容器（原生 InventoryType 的数值）。</summary>
    public int Container { get; set; }

    /// <summary>是否成功（原生 DiscardItem 返回 0 视为成功）。</summary>
    public bool Success { get; set; }

    /// <summary>备注（失败原因 / 跳过原因 / 策略说明）。</summary>
    public string Note { get; set; } = string.Empty;

    public DiscardLogEntry()
    {
    }

    public DiscardLogEntry(DateTime time, uint itemId, string itemName, uint quantity, int container, bool success, string note)
    {
        Time = time;
        ItemId = itemId;
        ItemName = itemName;
        Quantity = quantity;
        Container = container;
        Success = success;
        Note = note;
    }
}
