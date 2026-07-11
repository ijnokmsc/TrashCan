using System;
using System.Collections.Generic;
using Dalamud.Game.Inventory;
using Dalamud.Game.Inventory.InventoryEventArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using AutoTrash.Core;

namespace AutoTrash.Services;

/// <summary>
/// 背包变更监听（重构版）：
/// 订阅 IGameInventory.InventoryChanged，但“物品获得(ItemAdded)”事件只把候选记录到 pendingItems，
/// 不再实时丢弃；真正的丢弃由定时扫描（OnFrameworkUpdate 节流触发）或手动扫描（TriggerScan）统一执行。
///
/// 设计核心——“事件只记录、扫描才触发丢弃”：
///   1) 高频背包变化时，事件回调只做轻量字典写入，避免逐件实时丢弃带来的抖动与误丢风险；
///   2) 丢弃动作收敛到扫描时刻（定时或手动），由 AutoDiscardService 的入队/节流逻辑批量处理。
/// </summary>
public class InventoryWatcher : IDisposable
{
    private readonly IGameInventory gameInventory;
    private readonly IFramework framework;
    private readonly IClientState clientState;
    private readonly Configuration config;
    private readonly TrashListStore trashListStore;
    private readonly ItemResolver itemResolver;

    /// <summary>
    /// 有物品待处理时触发。每次扫描（TriggerScan）会逐件构造 PendingDiscard 并通过此事件入队。
    /// 签名保持与旧版一致（Action&lt;PendingDiscard&gt;），便于 Plugin 直接接线到 AutoDiscardService.Enqueue，
    /// 无需改动 AutoDiscardService 接线。
    /// </summary>
    public event Action<PendingDiscard>? ItemPending;

    /// <summary>
    /// 扫描触发信号：TriggerScan 在有待处理条目、正式入队前触发，供外部监听“一次扫描已开始”。
    /// </summary>
    public event Action? ScanRequested;

    /// <summary>
    /// 获得物品候选表。Key = (container &lt;&lt; 16) | slot（唯一标识容器+槽位）；
    /// Value = 物品关键信息（ItemId/Quantity/IsHq）。扫描后整体清空。
    /// </summary>
    private readonly Dictionary<int, (uint ItemId, int Quantity, bool IsHq)> pendingItems = new();

    private DateTime lastScan = DateTime.MinValue;

    public InventoryWatcher(IGameInventory gameInventory, IFramework framework, IClientState clientState, Configuration config, TrashListStore trashListStore, ItemResolver itemResolver)
    {
        this.gameInventory = gameInventory;
        this.framework = framework;
        this.clientState = clientState;
        this.config = config;
        this.trashListStore = trashListStore;
        this.itemResolver = itemResolver;
    }

    /// <summary>启用监听（事件订阅 + 帧更新定时器）。</summary>
    public void Enable()
    {
        gameInventory.InventoryChanged += OnInventoryChanged;
        framework.Update += OnFrameworkUpdate;
    }

    /// <summary>停用监听（取消事件订阅 + 帧更新）。</summary>
    public void Disable()
    {
        gameInventory.InventoryChanged -= OnInventoryChanged;
        framework.Update -= OnFrameworkUpdate;
    }

    /// <summary>
    /// 背包变更事件回调：仅记录“物品获得(ItemAdded)”事件，不立即丢弃。
    /// 使用模式匹配只处理 InventoryItemAddedArgs；ItemRemoved / ItemChanged / ItemMoved 等一律忽略。
    /// </summary>
    private void OnInventoryChanged(IReadOnlyCollection<InventoryEventArgs> events)
    {
        if (!clientState.IsLoggedIn || !config.EnableAddedItemDetection)
        {
            return;
        }

        foreach (var e in events)
        {
            // 仅处理“物品获得”子类型；其余事件不记录（避免重复或误丢）
            if (e is not InventoryItemAddedArgs addedEvent)
            {
                continue;
            }

            // 容器白名单过滤（与旧版一致）
            var container = (InventoryType)(int)addedEvent.Inventory;
            if (!Constants.IsEligibleContainer(container))
            {
                continue;
            }

            var item = addedEvent.Item;
            if (item.ItemId == 0 || item.IsEmpty)
            {
                continue;
            }

            // Key 唯一标识 容器+槽位；同槽新获得物品覆盖旧记录
            var key = MakeKey((int)addedEvent.Inventory, item.InventorySlot);
            pendingItems[key] = (item.ItemId, item.Quantity, item.IsHq);
        }
    }

    /// <summary>
    /// 框架帧回调（主线程）：若未开启“仅手动扫描”，且距上次扫描 >= ScanIntervalSeconds，
    /// 则触发一次扫描。原全量轮询（PollContainers）逻辑已移除。
    /// </summary>
    private void OnFrameworkUpdate(IFramework fw)
    {
        if (!clientState.IsLoggedIn || config.ManualScanOnly)
        {
            return;
        }

        var intervalSeconds = Math.Max(1, config.ScanIntervalSeconds);
        if ((DateTime.Now - lastScan) < TimeSpan.FromSeconds(intervalSeconds))
        {
            return;
        }

        lastScan = DateTime.Now;
        TriggerScan();
    }

    /// <summary>
    /// 执行一次扫描：
    ///   阶段一 —— 处理事件记录缓存（pendingItems）：将其中“获得物品”构造为 PendingDiscard 并逐件触发 ItemPending，随后清空。
    ///   阶段二 —— 主动扫描背包 1~4：即使 pendingItems 为空，也遍历 EligibleContainers 读取实际背包物品，
    ///            将命中丢弃列表的物品补构造为 PendingDiscard 并触发 ItemPending。
    ///            这样在「立即扫描」或「关闭窗口」时，背包里已存在、但未被 ItemAdded 事件记录的物品也能被扫描丢弃。
    /// 必须在主线程调用（OnFrameworkUpdate 与 ImGui 按钮均运行于主线程，天然安全）。
    /// </summary>
    public void TriggerScan()
    {
        // 未登录时不读取背包（避免无效数据与误判）
        if (!clientState.IsLoggedIn)
        {
            return;
        }

        // —— 阶段一：处理 pendingItems（保持原有逻辑：先 ScanRequested，再入队，最后清空） ——
        ScanRequested?.Invoke();

        foreach (var kvp in pendingItems)
        {
            SplitKey(kvp.Key, out var container, out var slot);
            var value = kvp.Value;

            var pd = new PendingDiscard
            {
                Container = container,
                Slot = (ushort)slot,
                ItemId = value.ItemId,
                Quantity = value.Quantity,
                IsHq = value.IsHq,
            };

            ItemPending?.Invoke(pd);
        }

        // 扫描完成，清空候选（无论是否有订阅者，状态都应复位）
        pendingItems.Clear();

        // —— 阶段二：主动扫描背包 1~4，补抓已存在但未被事件记录的物品 ——
        // 即使 pendingItems 为空也执行（如手动点击“立即扫描”/关闭窗口时），确保列表命中物品被扫描出来丢弃。
        foreach (var container in Constants.EligibleContainers)
        {
            try
            {
                var items = gameInventory.GetInventoryItems((GameInventoryType)(int)container);
                foreach (var item in items)
                {
                    if (item.ItemId == 0 || item.IsEmpty)
                    {
                        continue;
                    }

                    var itemName = itemResolver.GetName(item.ItemId);
                    if (!trashListStore.Contains(item.ItemId, itemName))
                    {
                        continue;
                    }

                    var pd = new PendingDiscard
                    {
                        Container = (int)container,
                        Slot = (ushort)item.InventorySlot,
                        ItemId = item.ItemId,
                        Quantity = item.Quantity,
                        IsHq = item.IsHq,
                    };

                    ItemPending?.Invoke(pd);
                }
            }
            catch
            {
                // 容器不可访问（尚未加载等）时忽略，不影响其余容器扫描
            }
        }
    }

    /// <summary>构造 pendingItems 的键：(container &lt;&lt; 16) | slot（slot 取低 16 位）。</summary>
    private static int MakeKey(int container, uint slot)
    {
        return (container << 16) | (int)(slot & 0xFFFF);
    }

    /// <summary>从键还原 container 与 slot。</summary>
    private static void SplitKey(int key, out int container, out int slot)
    {
        container = (key >> 16) & 0xFFFF;
        slot = key & 0xFFFF;
    }

    public void Dispose()
    {
        Disable();
    }
}
