using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;
using Dalamud.Plugin;
using Newtonsoft.Json;
using AutoTrash.Core;
using AutoTrash.Models;

namespace AutoTrash;

/// <summary>
/// 插件配置，持久化到 Dalamud 插件配置目录（通过 IDalamudPluginInterface.SavePluginConfig）。
/// </summary>
public class Configuration : IPluginConfiguration
{
    /// <summary>配置版本号（IPluginConfiguration 要求）。</summary>
    public int Version { get; set; } = 1;

    /// <summary>总开关：是否启用自动丢弃。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>是否已显示过首次使用警告（默认 false，仅首次打开主窗口时弹一次）。</summary>
    public bool HasShownWarning { get; set; } = false;

    /// <summary>是否丢弃 HQ 物品（默认 false，即保护 HQ）。</summary>
    public bool DiscardHq { get; set; } = false;

    /// <summary>特殊物品（装备）类型保护总开关：默认开，拦截列表中的装备被无确认丢弃。</summary>
    public bool ProtectSpecialItems { get; set; } = true;

    /// <summary>细分开关：是否允许丢弃装备（默认 false，即不允许）。仅当 ProtectSpecialItems 关闭或本开关开启时，装备才照常处理。</summary>
    public bool AllowDiscardEquip { get; set; } = false;

    /// <summary>
    /// 是否在背包原生界面注入垃圾桶按钮（默认开）。
    /// 与 Enabled 解耦：无论是否启用自动丢弃，点击按钮都打开主窗口（等同 /atrash）。
    /// 设为 false 可彻底隐藏该原生按钮。
    /// </summary>
    public bool ShowInventoryButton { get; set; } = true;

    /// <summary>
    /// 背包垃圾桶按钮使用的游戏图标 ID（默认 0 表示使用 FontAwesome 垃圾桶图标）。
    /// 非 0 时优先加载该游戏图标；若加载失败或 ID 为 0，则回退到 FontAwesome 图标。
    /// </summary>
    public uint InventoryButtonIconId { get; set; } = 0u;

    /// <summary>背包垃圾桶按钮缩放倍数（默认 1.0，范围 0.5~2.0）。</summary>
    public float InventoryButtonScale { get; set; } = 1.0f;

    /// <summary>背包垃圾桶按钮相对背包左侧吸附位置的像素偏移（默认 Vector2.Zero 即紧贴左侧）。</summary>
    public Vector2 InventoryButtonOffset { get; set; } = new Vector2(0f, 0f);

    /// <summary>是否在松开右键后将背包垃圾桶按钮重新吸附回背包左侧（默认 true）。</summary>
    public bool InventoryButtonSnapLeft { get; set; } = true;

    /// <summary>
    /// 是否一直显示背包垃圾桶按钮（默认 false）。
    /// 为 true 时按钮常驻屏幕、不随背包开关显隐，也不做背包锚定与吸附（不跟随背包移动、松开不回弹）。
    /// 为 false 时仅在背包打开时显示，并吸附在背包左侧（见 InventoryButtonSnapLeft）。
    /// </summary>
    public bool InventoryButtonAlwaysShow { get; set; } = false;
    public QuantityMode Mode { get; set; } = QuantityMode.DiscardAll;

    /// <summary>数量阈值（配合 Mode 使用）。</summary>
    public int QuantityThreshold { get; set; } = 0;

    /// <summary>
    /// 是否监听 InventoryChanged 的“物品获得”事件来记录待丢弃候选。
    /// 默认 true：获得物品时记录到 pendingItems。
    /// 设为 false 则完全不响应背包变更事件（手动/定时扫描将无数据来源，自动检测实质上禁用）。
    /// </summary>
    public bool EnableAddedItemDetection { get; set; } = true;

    /// <summary>
    /// 仅手动扫描：为 true 时关闭定时扫描，丢弃只在用户点击“立即扫描”按钮（MainWindow 列表页）时触发。
    /// 默认 false：启用定时扫描（按 ScanIntervalSeconds 周期触发）。
    /// </summary>
    public bool ManualScanOnly { get; set; } = false;

    /// <summary>
    /// 定时扫描间隔（秒），范围 &gt;= 1。仅当 ManualScanOnly = false 时生效。
    /// UI 中 InputInt 已限制最小值 1。
    /// </summary>
    public int ScanIntervalSeconds { get; set; } = 5;

    /// <summary>背包网格展示模式：Compact（紧凑）/ Standard（标准）/ Relaxed（宽松）。</summary>
    public string GridMode { get; set; } = "Standard";

    /// <summary>待丢弃物品列表。</summary>
    public List<TrashItemEntry> TrashList { get; set; } = new();

    /// <summary>丢弃日志（滚动保留，受 LogCap 限制）。</summary>
    public List<DiscardLogEntry> DiscardLog { get; set; } = new();

    /// <summary>日志滚动上限（建议 500）。</summary>
    public int LogCap { get; set; } = 500;

    /// <summary>用于 Save 的插件接口引用（不序列化）。</summary>
    [JsonIgnore]
    private IDalamudPluginInterface? pluginInterface;

    /// <summary>装配插件接口，供 Save 使用。</summary>
    public void Initialize(IDalamudPluginInterface pi)
    {
        pluginInterface = pi;
    }

    /// <summary>持久化当前配置。</summary>
    public void Save()
    {
        pluginInterface?.SavePluginConfig(this);
    }
}
