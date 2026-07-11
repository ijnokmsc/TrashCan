using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Conditions;
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
