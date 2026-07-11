using AutoTrash.Core;
using AutoTrash.Models;
using AutoTrash.Services;
using AutoTrash.Tests.Stubs;
using FFXIVClientStructs.FFXIV.Client.Game;
using Xunit;

namespace AutoTrash.Tests;

/// <summary>
/// 数量策略（QuantityMode）判定复核。
///
/// 说明：AutoDiscardService.Process 中的“是否丢弃 / 数量比较 / excess 拆分”逻辑是与
/// config + executor + logStore 内联耦合的，无法直接单元测试“真正执行原生丢弃”的分支
/// （那会调用 InventoryManager.Instance()->DiscardItem/SplitItem，需要游戏运行时）。
///
/// 因此本测试分两层：
///  1) 验证 QuantityMode 枚举值与设计一致（DiscardAll=0 / DiscardAboveThreshold=1 / KeepBelowThreshold=2）；
///  2) 用 FakeFramework/FakeClientState 在离线环境真实驱动 Process 的“跳过分支”
///     （容器不在白名单 / HQ 受保护 / 数量未超阈值），
///     覆盖 IsEligibleContainer + HQ + 阈值判定的内联逻辑，且不触碰任何原生 API。
///     注：鞍袋已移出白名单，不再有“鞍袋保护”分支。
///
/// “真正执行原生丢弃”的分支（DiscardAll 整堆丢、超阈值整堆丢、KeepBelowThreshold 拆分 excess）
/// 为运行时待验证项，详见测试报告“遗留风险”。
/// </summary>
public class QuantityStrategyTests
{
    [Fact]
    public void QuantityMode_EnumValues_MatchDesign()
    {
        Assert.Equal(0, (int)QuantityMode.DiscardAll);
        Assert.Equal(1, (int)QuantityMode.DiscardAboveThreshold);
        Assert.Equal(2, (int)QuantityMode.KeepBelowThreshold);
    }

    private sealed class Harness
    {
        public Configuration Cfg { get; } = new();
        public FakeFramework Framework { get; } = new();
        public FakeClientState Client { get; }
        public LogStore Log { get; }
        public TrashListStore TrashList { get; }
        public AutoDiscardService Svc { get; }

        public Harness()
        {
            Client = new FakeClientState { IsLoggedIn = true };
            Log = new LogStore(Cfg);
            TrashList = new TrashListStore(Cfg);
            // 真实 DiscardExecutor：本测试只走“跳过分支”，不会真正调用原生 InventoryManager。
            // 列表闸门修复后构造函数扩为 7 参，需传入 TrashListStore 与 ItemResolver
            // （IDataManager 传 null，GetName 内部 try/catch 安全降级返回 ItemId 字符串，不影响精确匹配）。
            Svc = new AutoDiscardService(Cfg, new DiscardExecutor(), Log, Client, Framework, TrashList, new ItemResolver(null));
        }

        public void Run(PendingDiscard pd)
        {
            // 列表闸门（修复后位于容器检查之后、HQ/阈值之前）：待测物品须先加入列表，
            // 才能越过闸门命中下游分支（阈值未达 / HQ 受保护 / 容器不在白名单 / 鞍袋保护）。
            TrashList.Add(new TrashItemEntry(pd.ItemId, string.Empty, false));
            Svc.Enqueue(pd);
            Framework.Tick(); // 触发 OnFrameworkUpdate -> Process（安全跳过分支）
        }

        public DiscardLogEntry LastLog() => Log.Entries[Log.Entries.Count - 1];
    }

    [Fact]
    public void DiscardAboveThreshold_QuantityBelowThreshold_Skips()
    {
        var h = new Harness();
        h.Cfg.Mode = QuantityMode.DiscardAboveThreshold;
        h.Cfg.QuantityThreshold = 5;
        h.Run(new PendingDiscard { Container = (int)InventoryType.Inventory1, Slot = 0, ItemId = 11, Quantity = 3, IsHq = false });
        // 需求 #3：数量未达阈值跳过，不记录任何日志
        Assert.Empty(h.Log.Entries);
    }

    [Fact]
    public void KeepBelowThreshold_QuantityBelowThreshold_Skips()
    {
        var h = new Harness();
        h.Cfg.Mode = QuantityMode.KeepBelowThreshold;
        h.Cfg.QuantityThreshold = 5;
        h.Run(new PendingDiscard { Container = (int)InventoryType.Inventory1, Slot = 1, ItemId = 12, Quantity = 4, IsHq = false });
        // 需求 #3：数量未达阈值跳过，不记录任何日志
        Assert.Empty(h.Log.Entries);
    }

    [Fact]
    public void IneligibleContainer_SkipsWithReason()
    {
        var h = new Harness();
        h.Cfg.Mode = QuantityMode.DiscardAll;
        h.Run(new PendingDiscard { Container = (int)InventoryType.EquippedItems, Slot = 0, ItemId = 13, Quantity = 1, IsHq = false });
        // 需求 #3：容器不在白名单跳过，不记录任何日志
        Assert.Empty(h.Log.Entries);
    }

    [Fact]
    public void HqProtectedByDefault_Skips()
    {
        var h = new Harness();
        h.Cfg.Mode = QuantityMode.DiscardAll;
        h.Cfg.DiscardHq = false; // 默认保护 HQ
        h.Run(new PendingDiscard { Container = (int)InventoryType.Inventory1, Slot = 0, ItemId = 14, Quantity = 1, IsHq = true });
        // 需求 #3：HQ 受保护跳过，不记录任何日志
        Assert.Empty(h.Log.Entries);
    }
}
