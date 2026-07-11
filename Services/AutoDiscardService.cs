using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using AutoTrash.Core;
using AutoTrash.Models;

namespace AutoTrash.Services;

/// <summary>
/// 一次待丢弃任务（已解析为容器/槽位/数量/HQ）。
/// </summary>
public class PendingDiscard
{
    /// <summary>原生 InventoryType 数值。</summary>
    public int Container { get; set; }

    /// <summary>槽位（ushort）。</summary>
    public ushort Slot { get; set; }

    /// <summary>物品 ItemId。</summary>
    public uint ItemId { get; set; }

    /// <summary>数量。</summary>
    public int Quantity { get; set; }

    /// <summary>是否 HQ。</summary>
    public bool IsHq { get; set; }
}

/// <summary>
/// 丢弃编排：在 IFramework.Update 安全帧内按规则（Enabled/列表命中/HQ/数量策略）执行丢弃。
/// </summary>
public class AutoDiscardService : IDisposable
{
    private readonly Configuration config;
    private readonly DiscardExecutor executor;
    private readonly LogStore logStore;
    private readonly IClientState clientState;
    private readonly IFramework framework;
    private readonly TrashListStore trashListStore;
    private readonly ItemResolver itemResolver;

    private readonly Queue<PendingDiscard> pending = new();
    private readonly object lockObj = new();
    private DateTime lastDiscard = DateTime.MinValue;
    private readonly TimeSpan discardInterval = TimeSpan.FromMilliseconds(300);

    /// <summary>暂停标记：主窗口打开期间置 true，停止执行任何自动/定时/扫描丢弃；关闭后恢复为 false。</summary>
    public bool Paused { get; set; } = false;

    public AutoDiscardService(Configuration config, DiscardExecutor executor, LogStore logStore, IClientState clientState, IFramework framework, TrashListStore trashListStore, ItemResolver itemResolver)
    {
        this.config = config;
        this.executor = executor;
        this.logStore = logStore;
        this.clientState = clientState;
        this.framework = framework;
        this.trashListStore = trashListStore;
        this.itemResolver = itemResolver;
        framework.Update += OnFrameworkUpdate;
    }

    /// <summary>入队一个待丢弃任务（同容器同槽位去重）。</summary>
    public void Enqueue(PendingDiscard item)
    {
        if (item == null)
        {
            return;
        }

        lock (lockObj)
        {
            foreach (var p in pending)
            {
                if (p.Container == item.Container && p.Slot == item.Slot)
                {
                    return;
                }
            }

            pending.Enqueue(item);
        }
    }

    /// <summary>框架帧回调：节流处理队列中的一项。</summary>
    private void OnFrameworkUpdate(IFramework fw)
    {
        if (!config.Enabled || !clientState.IsLoggedIn || Paused)
        {
            return;
        }

        if ((DateTime.Now - lastDiscard) < discardInterval)
        {
            return;
        }

        PendingDiscard? item = null;
        lock (lockObj)
        {
            if (pending.Count > 0)
            {
                item = pending.Dequeue();
            }
        }

        if (item == null)
        {
            return;
        }

        Process(item);
        lastDiscard = DateTime.Now;
    }

    /// <summary>规则判定并执行原生丢弃。</summary>
    private void Process(PendingDiscard item)
    {
        if (!config.Enabled)
        {
            return;
        }

        // 暂停期间不执行任何丢弃（主窗口打开时由 MainWindow 置位，防御性兜底）
        if (Paused)
        {
            return;
        }

        var container = (InventoryType)item.Container;

        // 容器白名单（仅背包 1~4）
        if (!Constants.IsEligibleContainer(container))
        {
            return;
        }

        // 列表命中判定（核心闸门）：仅列表内物品才进入丢弃流程
        var itemName = itemResolver.GetName(item.ItemId);
        if (!trashListStore.Contains(item.ItemId, itemName))
        {
            return;
        }

        // 特殊物品（装备）类型保护：默认拦截，需用户显式允许才丢
        if (config.ProtectSpecialItems && !config.AllowDiscardEquip && itemResolver.IsEquipItem(item.ItemId))
        {
            return;
        }

        // HQ 保护
        if (item.IsHq && !config.DiscardHq)
        {
            return;
        }

        int discardResult;
        switch (config.Mode)
        {
            case QuantityMode.DiscardAll:
                discardResult = executor.Discard(container, item.Slot);
                break;

            case QuantityMode.DiscardAboveThreshold:
                if (item.Quantity > config.QuantityThreshold)
                {
                    discardResult = executor.Discard(container, item.Slot);
                }
                else
                {
                    return;
                }

                break;

            case QuantityMode.KeepBelowThreshold:
                if (item.Quantity > config.QuantityThreshold)
                {
                    var excess = item.Quantity - config.QuantityThreshold;
                    discardResult = executor.SplitAndDiscard(container, item.Slot, excess);
                }
                else
                {
                    return;
                }

                break;

            default:
                discardResult = executor.Discard(container, item.Slot);
                break;
        }

        // 仅在“实际执行丢弃且原生返回 0（成功）”时记录日志；
        // 所有跳过（容器/列表/装备/HQ/阈值未达）与失败均不记录，避免日志被无意义的跳过信息刷屏。
        if (discardResult == 0)
        {
            logStore.Append(new DiscardLogEntry(
                DateTime.Now,
                item.ItemId,
                itemName,
                (uint)item.Quantity,
                item.Container,
                true,
                $"丢弃 {itemName} x{item.Quantity}"));
        }
    }

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
    }
}
