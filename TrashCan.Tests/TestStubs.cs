using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Inventory;
using Dalamud.Game.Inventory.InventoryEventArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AutoTrash.Tests.Stubs;

/// <summary>
/// 最小化的 IClientState 桩实现。
/// 仅让 <c>IsLoggedIn</c> 可读写（用于驱动 AutoDiscardService），其余成员返回安全默认值。
/// 用途：离线驱动 AutoDiscardService.Process 的“跳过分支”，不触发任何原生丢弃调用。
/// </summary>
public sealed class FakeClientState : IClientState
{
    public ClientLanguage ClientLanguage { get; set; }
    public uint TerritoryType { get; set; }
    public uint MapId { get; set; }
    public uint Instance { get; set; }
    public bool IsLoggedIn { get; set; }
    public bool IsPvP { get; set; }
    public bool IsPvPExcludingDen { get; set; }
    public bool IsGPosing { get; set; }

    public event Action<ZoneInitEventArgs>? ZoneInit;
    public event Action<uint>? TerritoryChanged;
    public event Action<uint>? MapIdChanged;
    public event Action<uint>? InstanceChanged;
    public event IClientState.ClassJobChangeDelegate? ClassJobChanged;
    public event IClientState.LevelChangeDelegate? LevelChanged;
    public event Action? Login;
    public event IClientState.LogoutDelegate? Logout;
    public event Action? EnterPvP;
    public event Action? LeavePvP;
    public event Action<Lumina.Excel.Sheets.ContentFinderCondition>? CfPop;

    public bool IsClientIdle(out ConditionFlag blockingFlag) { blockingFlag = default; return false; }
    public bool IsClientIdle() => false;
}

/// <summary>
/// 最小化的 IFramework 桩实现。
/// 捕获 <c>Update</c> 事件订阅，并通过 <see cref="Tick"/> 手动触发一帧，
/// 从而驱动 AutoDiscardService.OnFrameworkUpdate（即实际执行 Process 的安全路径）。
/// </summary>
public sealed class FakeFramework : IFramework
{
    public DateTime LastUpdate { get; set; }
    public DateTime LastUpdateUTC { get; set; }
    public TimeSpan UpdateDelta { get; set; }
    public bool IsInFrameworkUpdateThread { get; set; }
    public bool IsFrameworkUnloading { get; set; }

    private IFramework.OnUpdateDelegate? _update;

    public event IFramework.OnUpdateDelegate? Update
    {
        add => _update += value;
        remove => _update -= value;
    }

    /// <summary>手动触发一帧 Framework.Update，驱动已订阅的 OnFrameworkUpdate 回调。</summary>
    public void Tick() => _update?.Invoke(this);

    public TaskFactory GetTaskFactory() => Task.Factory;
    public Task DelayTicks(long numTicks, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Run(Action action, CancellationToken cancellationToken) { action(); return Task.CompletedTask; }
    public Task<T> Run<T>(Func<T> func, CancellationToken cancellationToken) => Task.FromResult(func());
    public Task Run(Func<Task> func, CancellationToken cancellationToken) => func();
    public Task<T> Run<T>(Func<Task<T>> func, CancellationToken cancellationToken) => func();
    public Task RunOnFrameworkThread(Action action) { action(); return Task.CompletedTask; }
    public Task<T> RunOnFrameworkThread<T>(Func<T> func) => Task.FromResult(func());
    public Task RunOnFrameworkThread(Func<Task> func) => func();
    public Task<T> RunOnFrameworkThread<T>(Func<Task<T>> func) => func();
    public Task RunOnTick(Action action, TimeSpan delay, int delayTicks, CancellationToken cancellationToken) { action(); return Task.CompletedTask; }
    public Task<T> RunOnTick<T>(Func<T> func, TimeSpan delay, int delayTicks, CancellationToken cancellationToken) => Task.FromResult(func());
    public Task RunOnTick(Func<Task> func, TimeSpan delay, int delayTicks, CancellationToken cancellationToken) => func();
    public Task<T> RunOnTick<T>(Func<Task<T>> func, TimeSpan delay, int delayTicks, CancellationToken cancellationToken) => func();
}

/// <summary>
/// IGameInventory 的最小化测试替身。
/// <list type="bullet">
///   <item><description>默认（Unreadable=false）：GetInventoryItems 返回空库存。单元测试不依赖实时背包槽位重定位，
///     用于覆盖“容器可读但目标已不在 -> 安全跳过”的分支，避免误丢旧槽位上的未授权物品（Bug 2 修复）。</description></item>
///   <item><description>Unreadable=true：GetInventoryItems 抛出 InvalidOperationException，模拟容器瞬时不可访问。
///     用于覆盖“容器不可读 -> 回退到扫描时记录的原始槽位继续丢弃”的分支，
///     保证既有 4 个触发真实 Discard 调用的回归测试在修复后仍可绿。</description></item>
/// </list>
/// 其余事件均作空实现（测试不依赖它们）。
/// 注：GameInventoryItem 的读取器走 native vtable，且为 readonly struct（属性全只读、仅无参构造），
/// 无法在无游戏进程的测试环境中伪造出可命中的实例，故无 Seed 逻辑；重定位（物品移位）覆盖率受此限制，见最终说明。
/// </summary>
public sealed class FakeGameInventory : IGameInventory
{
    /// <summary>模拟容器瞬时不可访问：为 true 时 GetInventoryItems 抛异常，触发回退到扫描时记录的原始槽位。</summary>
    public bool Unreadable { get; set; }

    public ReadOnlySpan<GameInventoryItem> GetInventoryItems(GameInventoryType type)
    {
        if (Unreadable)
        {
            throw new InvalidOperationException("simulated unreadable container");
        }

        return default; // 空库存（物品不可伪造）
    }

    public event IGameInventory.InventoryChangelogDelegate InventoryChanged;
    public event IGameInventory.InventoryChangelogDelegate InventoryChangedRaw;
    public event IGameInventory.InventoryChangedDelegate ItemAdded;
    public event IGameInventory.InventoryChangedDelegate ItemRemoved;
    public event IGameInventory.InventoryChangedDelegate ItemChanged;
    public event IGameInventory.InventoryChangedDelegate ItemMoved;
    public event IGameInventory.InventoryChangedDelegate ItemSplit;
    public event IGameInventory.InventoryChangedDelegate ItemMerged;
    public event IGameInventory.InventoryChangedDelegate<InventoryItemAddedArgs> ItemAddedExplicit;
    public event IGameInventory.InventoryChangedDelegate<InventoryItemRemovedArgs> ItemRemovedExplicit;
    public event IGameInventory.InventoryChangedDelegate<InventoryItemChangedArgs> ItemChangedExplicit;
    public event IGameInventory.InventoryChangedDelegate<InventoryItemMovedArgs> ItemMovedExplicit;
    public event IGameInventory.InventoryChangedDelegate<InventoryItemSplitArgs> ItemSplitExplicit;
    public event IGameInventory.InventoryChangedDelegate<InventoryItemMergedArgs> ItemMergedExplicit;
}
