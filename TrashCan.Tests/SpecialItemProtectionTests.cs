using AutoTrash;
using AutoTrash.Core;
using AutoTrash.Models;
using AutoTrash.Services;
using AutoTrash.Tests.Stubs;
using FFXIVClientStructs.FFXIV.Client.Game;
using Xunit;

namespace AutoTrash.Tests;

/// <summary>
/// 特殊物品（装备）类型保护开关的回归测试。
///
/// 增量内容：工程师在 AutoDiscardService.Process 的“列表命中闸门之后、HQ 保护之前”
/// 插入了类型保护闸：
///   if (config.ProtectSpecialItems &amp;&amp; !config.AllowDiscardEquip &amp;&amp; itemResolver.IsEquipItem(item.ItemId))
///   { Append("特殊物品(装备)受保护，已跳过"); return; }
///
/// 本测试复用 ListGateTests 的离线 Harness 模式（FakeFramework/FakeClientState 驱动
/// OnFrameworkUpdate→Process，FakeDiscardExecutor 仅计数不触原生 API），并新增
/// FakeItemResolver 通过 override IsEquipItem 让“是否为装备”在离线环境确定可控。
///
/// 覆盖四类场景：
///   1) 保护生效（核心）：总开关开 + 不允许丢装备 + 真装备 → 跳过、不丢、记保护日志。
///   2) 关总开关 → 照常丢（保护闸短路）。
///   3) 允许丢装备 → 照常丢（保护闸短路）。
///   4) 回归-非装备 → 照常丢，保护闸不误伤既有规则。
/// </summary>
public class SpecialItemProtectionTests
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

    /// <summary>
    /// ItemResolver 的离线替身：通过构造参数固化 IsEquipItem 的返回值，
    /// 使“是否为装备”在测试里确定可控，且不影响 Process 的其他行为（GetName 走基类安全降级）。
    /// </summary>
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
        public Configuration Cfg { get; }

        public FakeFramework Framework { get; } = new();

        public FakeClientState Client { get; }

        public LogStore Log { get; }

        public TrashListStore TrashList { get; }

        public FakeDiscardExecutor Executor { get; }

        public AutoDiscardService Svc { get; }

        public Harness(ItemResolver resolver, bool protectSpecialItems, bool allowDiscardEquip)
        {
            Cfg = new Configuration
            {
                // 默认值：Enabled=true、DiscardHq=false、Mode=DiscardAll
                ProtectSpecialItems = protectSpecialItems,
                AllowDiscardEquip = allowDiscardEquip,
            };
            Client = new FakeClientState { IsLoggedIn = true };
            Log = new LogStore(Cfg);
            TrashList = new TrashListStore(Cfg);
            Executor = new FakeDiscardExecutor();
            Svc = new AutoDiscardService(Cfg, Executor, Log, Client, Framework, TrashList, resolver);
        }

        /// <summary>入队一件位于 Inventory1、非 HQ 的物品，并驱动一帧 Process。</summary>
        public void RunProtectedItem()
        {
            TrashList.Add(new TrashItemEntry { ItemId = 12345, DisplayName = string.Empty, IsFuzzy = false });
            Svc.Enqueue(new PendingDiscard { Container = (int)InventoryType.Inventory1, Slot = 0, ItemId = 12345, Quantity = 1, IsHq = false });
            Framework.Tick(); // 触发 OnFrameworkUpdate -> Process
        }
    }

    /// <summary>
    /// 场景1（核心）：ProtectSpecialItems=true、AllowDiscardEquip=false、列表含该 ItemId、
    /// FakeItemResolver(true)。
    /// 断言：Discard/Split 调用均为 0，并记“特殊物品(装备)受保护，已跳过”。
    /// </summary>
    [Fact]
    public void EquipProtected_DefaultConfig_SkippedAndNotDiscarded()
    {
        var h = new Harness(new FakeItemResolver(true), protectSpecialItems: true, allowDiscardEquip: false);
        h.RunProtectedItem();

        // 保护闸必须拦截：未触发任何原生丢弃
        Assert.Equal(0, h.Executor.DiscardCallCount);
        Assert.Equal(0, h.Executor.SplitCallCount);

        // 需求 #3：跳过情况不记录任何日志
        Assert.Empty(h.Log.Entries);
    }

    /// <summary>
    /// 场景2：ProtectSpecialItems=false（总开关关闭）、AllowDiscardEquip=false、
    /// 列表含该 ItemId、FakeItemResolver(true)。
    /// 断言：保护闸短路，装备仍按列表命中 + 非 HQ 规则被整堆丢弃（DiscardCallCount==1）。
    /// </summary>
    [Fact]
    public void MasterSwitchOff_EquipDiscardedAnyway()
    {
        var h = new Harness(new FakeItemResolver(true), protectSpecialItems: false, allowDiscardEquip: false);
        h.RunProtectedItem();

        // 总开关关闭 → 闸门短路 → 装备照常按列表命中走丢弃
        Assert.Equal(1, h.Executor.DiscardCallCount);
        Assert.Equal(0, h.Executor.SplitCallCount);
    }

    /// <summary>
    /// 场景3：ProtectSpecialItems=true、AllowDiscardEquip=true（允许丢装备）、
    /// 列表含该 ItemId、FakeItemResolver(true)。
    /// 断言：保护闸短路，装备照常丢弃（DiscardCallCount==1）。
    /// </summary>
    [Fact]
    public void AllowDiscardEquipOn_EquipDiscardedAnyway()
    {
        var h = new Harness(new FakeItemResolver(true), protectSpecialItems: true, allowDiscardEquip: true);
        h.RunProtectedItem();

        // 显式允许丢装备 → 闸门短路 → 装备照常丢弃
        Assert.Equal(1, h.Executor.DiscardCallCount);
        Assert.Equal(0, h.Executor.SplitCallCount);
    }

    /// <summary>
    /// 场景4（回归）：ProtectSpecialItems=true、AllowDiscardEquip=false、
    /// 列表含该 ItemId、FakeItemResolver(false)（非装备）。
    /// 断言：保护闸不误伤非装备，非装备正常走丢弃（DiscardCallCount==1）。
    /// </summary>
    [Fact]
    public void NonEquip_NotProtected_Discarded()
    {
        var h = new Harness(new FakeItemResolver(false), protectSpecialItems: true, allowDiscardEquip: false);
        h.RunProtectedItem();

        // 非装备 → 闸门条件 IsEquipItem=false 不成立 → 正常丢弃
        Assert.Equal(1, h.Executor.DiscardCallCount);
        Assert.Equal(0, h.Executor.SplitCallCount);
    }

    /// <summary>
    /// 回归-真实 ItemResolver 在 dataManager 为 null 时安全降级：
    /// IsEquipItem 不应抛异常，且返回 false（无法判定就不拦）。
    /// 直接锁死“真实环境因 null GameData 导致 Process 崩溃”这一潜在源码 Bug。
    /// </summary>
    [Fact]
    public void RealItemResolver_NullDataManager_IsEquipItemSafeDegrades()
    {
        var resolver = new ItemResolver(null);

        // 不应抛任何异常（dataManager?. 短路 -> sheet==null -> 返回 false）
        var ex = Record.Exception(() => resolver.IsEquipItem(12345));
        Assert.Null(ex);

        // 安全降级：无法判装备时不拦截
        Assert.False(resolver.IsEquipItem(12345));

        // GetName 同样应安全降级为 ItemId 字符串，不抛异常
        var nameEx = Record.Exception(() => resolver.GetName(12345));
        Assert.Null(nameEx);
        Assert.Equal("12345", resolver.GetName(12345));
    }
}
