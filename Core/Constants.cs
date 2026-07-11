using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AutoTrash.Core;

/// <summary>
/// 数量策略枚举。
/// </summary>
public enum QuantityMode
{
    /// <summary>始终整堆丢弃。</summary>
    DiscardAll = 0,

    /// <summary>仅当数量大于阈值时，整堆丢弃（超量即清空）。</summary>
    DiscardAboveThreshold = 1,

    /// <summary>当数量大于阈值时，仅丢弃超出部分（保留阈值数量）。</summary>
    KeepBelowThreshold = 2,
}

/// <summary>
/// 策略常量与容器白名单。
/// </summary>
public static class Constants
{
    /// <summary>
    /// 允许自动丢弃的容器白名单：仅背包 1~4 (0-3)。
    /// 其余容器（陆行鸟鞍囊、部队存储柜、军武库、信箱、关键道具、雇员、房屋等）一律保护，不被自动丢弃。
    /// </summary>
    public static readonly HashSet<InventoryType> EligibleContainers = new()
    {
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
    };

    /// <summary>判断容器是否在白名单内（仅背包 1~4）。</summary>
    public static bool IsEligibleContainer(InventoryType container)
    {
        return EligibleContainers.Contains(container);
    }
}
