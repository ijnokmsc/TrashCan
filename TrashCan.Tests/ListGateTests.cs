using AutoTrash.Core;
using AutoTrash.Models;
using AutoTrash.Services;
using AutoTrash.Tests.Stubs;
using FFXIVClientStructs.FFXIV.Client.Game;
using Xunit;

namespace AutoTrash.Tests;

/// <summary>
/// 列表闸门（TrashListStore 命中判定）回归测试。
///
/// 锁死“列表未接入丢弃引擎”这一严重 Bug 不再复发：修复前 AutoDiscardService.Process
/// 没有列表命中检查，导致白名单容器内所有非 HQ 物品被无条件丢弃；修复后在容器白名单检查
/// 之后、HQ 保护之前插入列表闸门——未命中丢弃列表的物品一律不丢。
///
/// 本测试通过 FakeFramework/FakeClientState 离线驱动 Process 的安全路径（framework.Update
/// -> OnFrameworkUpdate -> Process），并用 FakeDiscardExecutor 替代原生 DiscardExecutor，
/// 仅累加调用计数、返回 0（视为成功），不触碰任何原生 InventoryManager API。
/// </summary>
public class ListGateTests
{
    /// <summary>
    /// DiscardExecutor 的测试替身：只累加调用计数并返回 0（视为原生成功），
    /// 完全不触碰原生 InventoryManager，使断言可聚焦于“是否触发了丢弃”。
    /// </summary>
    private sealed class FakeDiscardExecutor : DiscardExecutor
    {
        public int DiscardCallCount { get; private set; }

        public int SplitCallCount { get; private set; }

        public override int Discard(InventoryType container, ushort slot)
        {
            DiscardCallCount++;
            return 0;
        }

        public override int SplitAndDiscard(InventoryType container, ushort slot, int excess)
        {
            SplitCallCount++;
            return 0;
        }
    }

    private sealed class Harness
    {
        public Configuration Cfg { get; } = new();
        public FakeFramework Framework { get; } = new();
        public FakeClientState Client { get; }
        public LogStore Log { get; }
        public TrashListStore TrashList { get; }
        public FakeDiscardExecutor Executor { get; }
        public AutoDiscardService Svc { get; }

        public Harness()
        {
            Client = new FakeClientState { IsLoggedIn = true };
            Log = new LogStore(Cfg);
            TrashList = new TrashListStore(Cfg);
            Executor = new FakeDiscardExecutor();
            // ItemResolver 传 null 作为 IDataManager：GetName 内部 try/catch 安全降级返回 ItemId 字符串，
            // 不影响精确 ItemId 匹配（Contains 对非模糊条目按 ItemId 精确比较）。
            Svc = new AutoDiscardService(Cfg, Executor, Log, Client, Framework, TrashList, new ItemResolver(null));
        }

        public void Run(PendingDiscard pd)
        {
            Svc.Enqueue(pd);
            Framework.Tick(); // 触发 OnFrameworkUpdate -> Process
        }

        public DiscardLogEntry LastLog() => Log.Entries[Log.Entries.Count - 1];
    }

    /// <summary>
    /// 场景1（核心回归）：待丢弃列表为空时，白名单容器内的非 HQ 物品绝不应被丢弃。
    /// 这正是被修复的严重 Bug 的逆向断言——若列表闸门失效，Discard 会被调用。
    /// </summary>
    [Fact]
    public void EmptyList_ItemSkipped_NotDiscarded()
    {
        var h = new Harness();
        // Cfg.TrashList 默认为空
        h.Run(new PendingDiscard { Container = (int)InventoryType.Inventory1, Slot = 0, ItemId = 12345, Quantity = 1, IsHq = false });

        // 列表闸门必须拦截：未调用任何原生丢弃
        Assert.Equal(0, h.Executor.DiscardCallCount);
        Assert.Equal(0, h.Executor.SplitCallCount);

        // 需求 #3：跳过情况不记录任何日志（日志只记录实际成功丢弃）
        Assert.Empty(h.Log.Entries);
    }

    /// <summary>
    /// 场景2：列表包含该 ItemId 时，物品应被丢弃（Discard 调用一次）并记成功日志。
    /// 验证列表闸门“放行”路径正确。
    /// </summary>
    [Fact]
    public void ListContainsItemId_ItemDiscarded()
    {
        var h = new Harness();
        h.TrashList.Add(new TrashItemEntry { ItemId = 12345, DisplayName = string.Empty, IsFuzzy = false });
        h.Run(new PendingDiscard { Container = (int)InventoryType.Inventory1, Slot = 0, ItemId = 12345, Quantity = 1, IsHq = false });

        // 列表命中 -> 触发一次整堆丢弃
        Assert.Equal(1, h.Executor.DiscardCallCount);
        Assert.Equal(0, h.Executor.SplitCallCount);

        // 且应记录一条成功日志（原生返回 0 视为成功）
        Assert.Contains(h.Log.Entries, e => e.Success && e.ItemId == 12345);
    }

    /// <summary>
    /// 场景3（回归-容器白名单仍生效）：列表含某 ItemId，但物品位于非白名单容器时跳过。
    /// 确认本次列表闸门改动没有破坏既有的容器白名单保护。
    /// </summary>
    [Fact]
    public void NonWhitelistedContainer_Skips_WhitelistStillWorks()
    {
        var h = new Harness();
        h.TrashList.Add(new TrashItemEntry { ItemId = 999, DisplayName = string.Empty, IsFuzzy = false });
        h.Run(new PendingDiscard { Container = (int)InventoryType.EquippedItems, Slot = 0, ItemId = 999, Quantity = 1, IsHq = false });

        // 容器检查在列表闸门之前，应优先拦截，且不触发丢弃
        Assert.Equal(0, h.Executor.DiscardCallCount);
        // 需求 #3：跳过情况不记录任何日志
        Assert.Empty(h.Log.Entries);
    }

    /// <summary>
    /// 场景4（回归-HQ 保护仍生效）：列表含某 HQ 物品的 ItemId，DiscardHq=false 时跳过。
    /// 确认本次列表闸门改动没有破坏既有的 HQ 保护。
    /// </summary>
    [Fact]
    public void HqProtected_Skips_HqProtectionStillWorks()
    {
        var h = new Harness();
        const uint hqItem = 7777;
        h.TrashList.Add(new TrashItemEntry { ItemId = hqItem, DisplayName = string.Empty, IsFuzzy = false });
        h.Cfg.DiscardHq = false; // 默认保护 HQ
        h.Run(new PendingDiscard { Container = (int)InventoryType.Inventory1, Slot = 0, ItemId = hqItem, Quantity = 1, IsHq = true });

        // HQ 检查在列表闸门之后、丢弃之前，应拦截，且不触发丢弃
        Assert.Equal(0, h.Executor.DiscardCallCount);
        // 需求 #3：跳过情况不记录任何日志
        Assert.Empty(h.Log.Entries);
    }
}
