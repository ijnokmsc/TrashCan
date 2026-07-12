using AutoTrash;
using AutoTrash.Core;
using AutoTrash.Models;
using AutoTrash.Services;
using AutoTrash.Tests.Stubs;
using FFXIVClientStructs.FFXIV.Client.Game;
using Xunit;

namespace AutoTrash.Tests;

/// <summary>
/// F1 条目级「保留数量」阈值的回归与行为测试。
///
/// 复用 ListGateTests / SpecialItemProtectionTests 的离线 Harness 模式：
/// FakeFramework/FakeClientState 驱动 OnFrameworkUpdate→Process，FakeDiscardExecutor 仅计数不触原生 API。
/// FakeGameInventory.Unreadable=true 触发 ResolveCurrentSlot 回退到扫描时记录的原始槽位，
/// 从而走真实丢弃路径（Discard/SplitAndDiscard 被 FakeDiscardExecutor 记录），保住本文件既有回归。
///
/// 覆盖：
///   1) 条目启用阈值 → 强制 KeepBelowThreshold + 条目阈值，且 excess = 当前数量 - 条目阈值（绝不超丢）。
///   2) 数量未达条目阈值 → 跳过、不丢、不记日志。
///   3) 条目未启用阈值 → 回退全局策略（DiscardAll / DiscardAboveThreshold）。
///   4) HQ 保护 / 装备保护在条目阈值场景下依然生效（不误伤既有规则）。
///   5) CSV / JSON 往返保留阈值字段；旧 3 列表头 CSV 向后兼容（默认 0 / false）。
/// </summary>
public class QuantityThresholdTests
{
    /// <summary>DiscardExecutor 测试替身：记录 Discard/SplitAndDiscard 调用次数与最后一次 Split 的 excess。</summary>
    private sealed class FakeDiscardExecutor : DiscardExecutor
    {
        public int DiscardCallCount { get; private set; }

        public int SplitCallCount { get; private set; }

        public int LastSplitExcess { get; private set; }

        /// <summary>每次 SplitAndDiscard 的 excess 历史（用于验证多槽位各自独立计算，绝不跨槽位聚合）。</summary>
        public List<int> SplitExcessHistory { get; } = new();

        public override int Discard(InventoryType container, ushort slot)
        {
            DiscardCallCount++;
            return 0;
        }

        public override int SplitAndDiscard(InventoryType container, ushort slot, int excess)
        {
            SplitCallCount++;
            LastSplitExcess = excess;
            SplitExcessHistory.Add(excess);
            return 0;
        }
    }

    /// <summary>ItemResolver 离线替身：固化 IsEquipItem 返回值，使「是否为装备」确定可控。</summary>
    private sealed class FakeItemResolver : ItemResolver
    {
        private readonly bool _isEquip;

        public FakeItemResolver(bool isEquip)
            : base(null)
        {
            _isEquip = isEquip;
        }

        public override bool IsEquipItem(uint itemId) => _isEquip;
    }

    private sealed class Harness
    {
        public Configuration Cfg { get; } = new();

        public FakeFramework Framework { get; } = new();

        public FakeClientState Client { get; } = new() { IsLoggedIn = true };

        public LogStore Log { get; }

        public TrashListStore TrashList { get; }

        public FakeDiscardExecutor Executor { get; }

        public AutoDiscardService Svc { get; }

        public Harness(ItemResolver resolver)
        {
            Log = new LogStore(Cfg);
            TrashList = new TrashListStore(Cfg);
            Executor = new FakeDiscardExecutor();
            // Unreadable=true：回退到扫描时记录的原始槽位，走真实丢弃路径
            Svc = new AutoDiscardService(Cfg, Executor, Log, Client, Framework, TrashList, resolver, new FakeGameInventory { Unreadable = true });
        }

        /// <summary>加入一个条目级阈值条目（按 ItemId 匹配）。</summary>
        public void AddEntry(uint itemId, int threshold, bool hasThreshold, bool isFuzzy = false)
        {
            TrashList.Add(new TrashItemEntry(itemId, string.Empty, isFuzzy)
            {
                HasThreshold = hasThreshold,
                QuantityThreshold = threshold,
            });
        }

        /// <summary>入队一件位于 Inventory1、非 HQ 的物品（除非显式指定），驱动一帧 Process。</summary>
        public void Run(uint itemId, int qty, bool isHq = false, int slot = 0, InventoryType container = InventoryType.Inventory1)
        {
            Svc.Enqueue(new PendingDiscard { Container = (int)container, Slot = (ushort)slot, ItemId = itemId, Quantity = qty, IsHq = isHq });
            Framework.Tick();
        }
    }

    /// <summary>核心：条目阈值强制 KeepBelowThreshold，且 excess = 当前数量 - 条目阈值（绝不超丢）。</summary>
    [Fact]
    public void EntryThreshold_ForcesKeepBelowThreshold_OnlyDiscardsExcess()
    {
        var h = new Harness(new ItemResolver(null));
        h.Cfg.Mode = QuantityMode.DiscardAll; // 全局会整堆丢，用于证明条目阈值确实覆盖了它
        h.AddEntry(1001, threshold: 5, hasThreshold: true);
        h.Run(1001, qty: 10, slot: 0);

        // 仅触 SplitAndDiscard，excess = 10 - 5 = 5；绝不整堆 Discard
        Assert.Equal(0, h.Executor.DiscardCallCount);
        Assert.Equal(1, h.Executor.SplitCallCount);
        Assert.Equal(5, h.Executor.LastSplitExcess);
    }

    /// <summary>
    /// 团队指定场景（PRD US-F1-1 / 验收标准）：阈值 N=10、当前 15 → 只丢 5、保留 10、未被全丢。
    /// 重点：不仅断言“调用了 SplitAndDiscard”，更断言实际 excess 计算（15-10=5）与剩余量（15-5=10==阈值），
    /// 真证伪“只丢超出、保留 N、不超丢”。全局 Mode=DiscardAll 用于证明条目阈值确实覆盖了它。
    /// </summary>
    [Fact]
    public void EntryThreshold_TeamLeadExample_Threshold10_Qty15_Discards5Keeps10()
    {
        var h = new Harness(new ItemResolver(null));
        h.Cfg.Mode = QuantityMode.DiscardAll; // 全局会整堆丢，用于证明条目阈值确实覆盖了它
        h.AddEntry(1010, threshold: 10, hasThreshold: true);
        h.Run(1010, qty: 15, slot: 0);

        // 只触 SplitAndDiscard，excess = 15 - 10 = 5；绝不整堆 Discard
        Assert.Equal(0, h.Executor.DiscardCallCount);
        Assert.Equal(1, h.Executor.SplitCallCount);
        Assert.Equal(5, h.Executor.LastSplitExcess);
        // 剩余 = 当前 - 丢弃 = 15 - 5 = 10（== 阈值，未被全丢、未超丢）
        Assert.Equal(10, 15 - h.Executor.LastSplitExcess);
    }

    /// <summary>
    /// 多槽位同物品按 slot 独立判定（PRD Q-F1-3 / 设计“安全不超丢”）：
    /// 同一 ItemId 分布在两个槽位（slot0 当前 15、slot1 当前 12），阈值均为 10。
    /// 期望：slot0 只丢超出 5、slot1 只丢超出 2，分别触发独立 Split，绝不跨槽位聚合（15+12=27 超 10 会丢 17）。
    /// 注意：OnFrameworkUpdate 有 300ms 节流，故两次 Run 之间需 sleep 以走真实丢弃路径。
    /// </summary>
    [Fact]
    public void EntryThreshold_MultiSlot_SameItem_IndependentPerSlot_NoOverDiscard()
    {
        var h = new Harness(new ItemResolver(null));
        h.Cfg.Mode = QuantityMode.DiscardAll;
        h.AddEntry(1011, threshold: 10, hasThreshold: true);

        // 同一物品分布在两个槽位：slot0 当前 15（超出 5）、slot1 当前 12（超出 2）
        h.Run(1011, qty: 15, slot: 0);
        System.Threading.Thread.Sleep(400); // 越过框架 300ms 节流，使第二次 Process 真实执行
        h.Run(1011, qty: 12, slot: 1);

        // 每个槽位独立计算 excess，分别只丢 5 与 2；共触发 2 次 Split，绝不整堆 Discard
        Assert.Equal(0, h.Executor.DiscardCallCount);
        Assert.Equal(2, h.Executor.SplitCallCount);
        Assert.Equal(new List<int> { 5, 2 }, h.Executor.SplitExcessHistory);
    }

    /// <summary>数量未达条目阈值 → 跳过、不丢、不记日志（零回归：与全局阈值跳过行为一致）。</summary>
    [Fact]
    public void EntryThreshold_QuantityBelowThreshold_Skips()
    {
        var h = new Harness(new ItemResolver(null));
        h.Cfg.Mode = QuantityMode.DiscardAll;
        h.AddEntry(1002, threshold: 10, hasThreshold: true);
        h.Run(1002, qty: 8);

        Assert.Equal(0, h.Executor.DiscardCallCount);
        Assert.Equal(0, h.Executor.SplitCallCount);
        Assert.Empty(h.Log.Entries);
    }

    /// <summary>条目未启用阈值（HasThreshold=false）→ 回退全局 DiscardAll，整堆丢弃。</summary>
    [Fact]
    public void NoEntryThreshold_FallsBackToGlobalDiscardAll()
    {
        var h = new Harness(new ItemResolver(null));
        h.Cfg.Mode = QuantityMode.DiscardAll;
        h.AddEntry(1003, threshold: 5, hasThreshold: false);
        h.Run(1003, qty: 1);

        Assert.Equal(1, h.Executor.DiscardCallCount);
        Assert.Equal(0, h.Executor.SplitCallCount);
    }

    /// <summary>条目未启用阈值 → 回退全局 DiscardAboveThreshold，超阈值整堆丢弃。</summary>
    [Fact]
    public void NoEntryThreshold_FallsBackToGlobalDiscardAboveThreshold_ExcessDiscarded()
    {
        var h = new Harness(new ItemResolver(null));
        h.Cfg.Mode = QuantityMode.DiscardAboveThreshold;
        h.Cfg.QuantityThreshold = 5;
        h.AddEntry(1006, threshold: 5, hasThreshold: false);
        h.Run(1006, qty: 10); // 10 > 5 → 整堆丢

        Assert.Equal(1, h.Executor.DiscardCallCount);
        Assert.Equal(0, h.Executor.SplitCallCount);
    }

    /// <summary>全局 DiscardAboveThreshold 下未达阈值 → 跳过（验证回退路径不误丢）。</summary>
    [Fact]
    public void NoEntryThreshold_FallsBackToGlobalDiscardAboveThreshold_BelowSkips()
    {
        var h = new Harness(new ItemResolver(null));
        h.Cfg.Mode = QuantityMode.DiscardAboveThreshold;
        h.Cfg.QuantityThreshold = 5;
        h.AddEntry(1007, threshold: 5, hasThreshold: false);
        h.Run(1007, qty: 3); // 3 <= 5 → 跳过

        Assert.Equal(0, h.Executor.DiscardCallCount);
        Assert.Equal(0, h.Executor.SplitCallCount);
    }

    /// <summary>HQ 保护在条目阈值场景下依然生效：HQ 物品不被丢弃。</summary>
    [Fact]
    public void EntryThreshold_HqProtected_Skips()
    {
        var h = new Harness(new ItemResolver(null));
        h.Cfg.DiscardHq = false;
        h.Cfg.Mode = QuantityMode.DiscardAll;
        h.AddEntry(1004, threshold: 5, hasThreshold: true);
        h.Run(1004, qty: 10, isHq: true);

        Assert.Equal(0, h.Executor.DiscardCallCount);
        Assert.Equal(0, h.Executor.SplitCallCount);
        Assert.Empty(h.Log.Entries);
    }

    /// <summary>装备保护在条目阈值场景下依然生效：装备不被丢弃。</summary>
    [Fact]
    public void EntryThreshold_EquipProtected_Skips()
    {
        var h = new Harness(new FakeItemResolver(true));
        h.Cfg.ProtectSpecialItems = true;
        h.Cfg.AllowDiscardEquip = false;
        h.Cfg.Mode = QuantityMode.DiscardAll;
        h.AddEntry(1005, threshold: 5, hasThreshold: true);
        h.Run(1005, qty: 10);

        Assert.Equal(0, h.Executor.DiscardCallCount);
        Assert.Equal(0, h.Executor.SplitCallCount);
    }

    /// <summary>CSV 往返保留条目级阈值字段。</summary>
    [Fact]
    public void CsvRoundTrip_PreservesThresholdFields()
    {
        var list = new List<TrashItemEntry>
        {
            new(2001, "Widget", false) { HasThreshold = true, QuantityThreshold = 7 },
        };
        var csv = ImportExport.ExportCsv(list);
        var back = ImportExport.ImportCsv(csv);

        Assert.Single(back);
        Assert.True(back[0].HasThreshold);
        Assert.Equal(7, back[0].QuantityThreshold);
    }

    /// <summary>JSON 往返保留条目级阈值字段。</summary>
    [Fact]
    public void JsonRoundTrip_PreservesThresholdFields()
    {
        var list = new List<TrashItemEntry>
        {
            new(2002, "Gizmo", false) { HasThreshold = true, QuantityThreshold = 3 },
        };
        var json = ImportExport.ExportJson(list);
        var back = ImportExport.ImportJson(json);

        Assert.Single(back);
        Assert.True(back[0].HasThreshold);
        Assert.Equal(3, back[0].QuantityThreshold);
    }

    /// <summary>向后兼容：旧 3 列表头 CSV（无阈值列）导入后默认 HasThreshold=false / QuantityThreshold=0。</summary>
    [Fact]
    public void CsvBackwardCompat_OldThreeColumnFormat()
    {
        const string oldCsv = "ItemId,DisplayName,IsFuzzy\n1001,Apple,false\n";
        var back = ImportExport.ImportCsv(oldCsv);

        Assert.Single(back);
        Assert.Equal(1001u, back[0].ItemId);
        Assert.Equal("Apple", back[0].DisplayName);
        Assert.False(back[0].IsFuzzy);
        Assert.False(back[0].HasThreshold);
        Assert.Equal(0, back[0].QuantityThreshold);
    }
}
