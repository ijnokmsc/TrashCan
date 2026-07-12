# AutoTrash（TrashCan）增量 PRD — v1.0.2

> 形态：**增量 PRD**（仅描述相对 v1.0.1 的两个新功能，不重写整体需求）
> 角色：产品经理 许清楚（Xu）
> 基线：v1.0.1（已发布）｜ 配套文档：PRD.md / Design.md / class-diagram.mermaid / sequence-diagram.mermaid
> 版本目标：AssemblyVersion `1.0.2.0`、Release tag `v1.0.2`、插件源 `pluginmaster.json` 同步

---

## 1. 增量概述

本次在 v1.0.1 基础上新增两个能力，**不改动既有自动丢弃主流程**：

1. **数量阈值丢弃规则（条目级）**：在「添加」页右键物品可输入一个「保留数量阈值」；当该物品在背包（Inventory1~4）中的数量超过阈值时，只丢超出的部分、精确保留阈值数量（例如阈值 10、当前 15 → 丢 5 留 10；后续变 18 → 再丢 8 留 10），**绝不超丢**。
2. **背包页垃圾桶按钮（原生 UI 注入）**：在游戏背包原生界面注入一个垃圾桶按钮，点击即打开 AutoTrash 主窗口（等同 `/atrash`）。用户已明确选择原生注入方案。

**为什么做**：
- Feature 1 解决「整堆清空 vs. 留一部分自用」的粒度问题：当前全局 `数量阈值` 策略（设置页）对所有列表物品一视同仁，无法对单件物品单独设定「留 N 个」。本次把阈值下沉到**条目级**。
- Feature 2 降低打开插件的操作成本：用户已在游戏背包里，无需切到聊天框敲 `/atrash` 或找 Dalamud 菜单，一键直达。

**版本变更（列入发布说明）**：
- `Properties/AssemblyInfo.cs`：`[assembly: AssemblyVersion("1.0.1.0")]` → `1.0.2.0`（同时更新 `AssemblyFileVersion` / `AssemblyInformationalVersion`）。
- `auto_trash_can.json`：`"AssemblyVersion": "1.0.1.0"` → `1.0.2.0`；描述可补一句「背包原生垃圾桶按钮」。
- GitHub Release tag：`v1.0.2`（3 段）。
- 插件源 `pluginmaster.json`（外部仓库 `ijnokmsc/DalamudPlugins`）同步版本与下载链接。
- 窗口标题 `GetVersionString()` 自动取 AssemblyVersion → 显示 `v1.0.2`，无需改代码。

---

## 2. 用户故事

### Feature 1 — 数量阈值丢弃规则
- **US-F1-1**：作为常刷副本的玩家，我希望把「破损的石板」加入丢弃列表时设定「只留 10 个」，这样进包超过 10 个的部分自动清掉、但始终保留 10 个自用，不用手动清理也不用被清空。
- **US-F1-2**：作为谨慎玩家，我希望阈值只在「添加」页右键时设定、且超量只丢超出部分，这样绝不会把背包里该留的数量误删（不超丢）。

### Feature 2 — 背包页垃圾桶按钮
- **US-F2-1**：作为正在整理背包的玩家，我希望在游戏背包界面直接看到一个垃圾桶按钮，点一下就打开 AutoTrash 设置/列表，这样不用退出背包去敲命令或翻 Dalamud 菜单。
- **US-F2-2**：作为插件用户，我希望这个按钮是游戏原生观感、不挡视线、只在背包打开时出现，这样它像游戏自带控件一样自然、不会破坏背包布局。

---

## 3. 需求池

### P0 — 必须实现（Must）

| ID | 需求 | 验收标准 |
|----|------|----------|
| P0-F1 | 条目级「保留数量阈值」字段 | `TrashItemEntry` 新增 `int QuantityThreshold`（默认 0）与 `bool HasThreshold`（默认 false）；旧配置/旧列表导入无该字段时按默认值处理，**行为不变**。 |
| P0-F1 | 添加页右键输入阈值并加项 | 「添加」页网格右键某物品 → 弹出输入框输入阈值 → 确认后将该物品以 `HasThreshold=true, QuantityThreshold=N` 加入列表；不输入/取消则行为同现状（普通加项）。 |
| P0-F1 | 条目级阈值生效（只丢超出部分、不超丢） | 对 `HasThreshold=true` 的条目，丢弃判定复用现有「保留阈值」语义：`excess = 当前数量 − N`，仅丢弃 `excess` 个；当前数量 ≤ N 时不动。多轮扫描后稳定保留 N 个。 |
| P0-F1 | 条目级阈值优先级高于全局策略 | `HasThreshold=true` 的条目使用自身阈值（保留语义）；`HasThreshold=false` 的条目回退到设置页全局 `Mode`/`QuantityThreshold`（维持 v1.0.1 行为）。 |
| P0-F2 | 背包原生界面注入垃圾桶按钮 | 游戏背包打开时，在原生背包界面上出现一个垃圾桶按钮（非浮动窗、非命令），仅背包可见时出现。 |
| P0-F2 | 点击按钮打开主窗口 | 点击该按钮效果等同 `/atrash`（切换打开 AutoTrash 主窗口）；复用现有 `MainWindow.Toggle()`，故打开期间自动暂停丢弃等既有行为保持。 |

### P1 — 应该实现（Should）

| ID | 需求 | 验收标准 |
|----|------|----------|
| P1-F1 | 添加页右键交互提示 | 「添加」页有文字提示「右键物品可设置保留数量」，让用户知道有该入口。 |
| P1-F1 | 列表页可查看/清除条目阈值 | 「列表」页网格悬停 tooltip 显示该条目的保留阈值（如「保留 10」）；提供清除阈值的方式（如再次右键 → 清空阈值归零 / 或列表页右键菜单）。 |
| P1-F1 | 默认阈值建议值 | 右键输入框初值建议为 `0` 或该物品当前数量的某个合理默认（如当前数量，表示先不丢），由架构/用户在待确认问题中拍板。 |
| P1-F2 | 按钮原生观感 | 按钮使用游戏原生控件风格（图标/配色接近背包自带按钮），不引入明显违和的 ImGui 浮层。 |
| P1-F2 | 按钮位置合理 | 注入到背包界面合适位置（建议背包头部操作区，靠近「整理/搜索」等原生按钮），不遮挡物品格、不破坏布局。 |
| P1-F2 | 按钮生命周期安全 | 背包 addon 加载时注入、卸载（Finalize）时移除；刷新/重排时不重复注入；插件卸载时彻底清理，无残留节点/内存泄漏。 |

### P2 — 可选实现（Nice to have）

| ID | 需求 | 验收标准 |
|----|------|----------|
| P2-F1 | 条目级策略多样化 | 除「保留 N」外，未来可扩展条目级「超阈值才整堆丢」等模式（复用现有 `QuantityMode` 枚举）。 |
| P2-F1 | CSV 导入导出新增阈值列 | `ExportCsv`/`ImportCsv` 表头追加 `QuantityThreshold[,HasThreshold]`，向后兼容旧 CSV（缺列则默认 0/false）。JSON 因 Newtonsoft 自动忽略缺字段，**天然兼容**，无需改动。 |
| P2-F2 | 按钮悬停 tooltip / 快捷键提示 | 悬停显示「打开 AutoTrash 自动丢弃（等同 /atrash）」。 |
| P2-F2 | 动画/高亮 | 按钮在插件启用时高亮、禁用时置灰等微交互（纯视觉，不影响功能）。 |

---

## 4. UI 设计稿

### Feature 1 — 「添加」页右键输入数量阈值

现有「添加」页是仿游戏背包网格（`DrawInventoryGridSection`），左键整格点击 = 加/移除列表。本次**新增右键**交互：

```
┌────────────────────────────────────────────────────┐
│ 仿游戏背包：点击格子加入 / 移除丢弃列表              │
│ 提示：右键物品可设置「保留数量」，仅留该数量、超量自动丢 │
│ [Compact][Standard][Relaxed]                         │
│ ┌────┬────┬────┬────┬────┐                          │
│ │🪨 │📜 │🪙 │    │    │   ← 左键=加/移除；右键=设阈值 │
│ └────┴────┴────┴────┴────┘                          │
└────────────────────────────────────────────────────┘

   右键某物品（如「破损的石板」）后弹出：

┌─────────────────────────────────────┐
│  设置保留数量：破损的石板            │
│  超过该数量时，只丢超出部分、保留： │
│  保留数量 [ 10        ]  （个）      │
│  ☑ 启用条目级阈值                   │
│  （取消勾选 = 跟随全局数量策略）     │
│                                      │
│  [ 确定 ]      [ 取消 ]              │
└─────────────────────────────────────┘
   确定 → 加入列表：HasThreshold=true, QuantityThreshold=10
   取消 → 不加入（或加入但不设阈值，视实现）
```

**实现提示**：
- `DrawItemGrid` 当前仅用 `InvisibleButton` 左键点击；需补充右键检测（`ImGui.IsMouseClicked(ImGuiMouseButton.Right)` 且 `ImGui.IsItemHovered()`），命中后 `ImGui.OpenPopup("设置保留数量")`。
- 弹窗内 `ImGui.InputInt("保留数量", ref thresholdInput)`；确认时构造 `new TrashItemEntry(itemId, name, false){ HasThreshold = true, QuantityThreshold = N }` 并 `listStore.Add(...)`。
- 「列表」页可对已加项右键 → 同样弹窗（用于修改/清除阈值）。

### Feature 2 — 背包页垃圾桶按钮位置建议

注入目标：**游戏背包原生窗口（背包 addon）**。建议依附于背包头部操作区（与「整理 / 搜索 / 排序」等原生按钮同排或紧邻），样式用原生控件（垃圾桶图标按钮）。

```
┌──────────────────────────────────────────────────┐
│  背包                            [_] [🔍] [🗑] [X] │  ← 🗑 = 新增 AutoTrash 原生按钮
├──────────────────────────────────────────────────┤
│  ┌────┬────┬────┬────┬────┬────┐                  │
│  │物品│物品│物品│物品│物品│物品│   ...（原生物品格）│
│  └────┴────┴────┴────┴────┴────┘                  │
└──────────────────────────────────────────────────┘
   点击 🗑 → 打开 AutoTrash 主窗口（等同 /atrash）
```

**实现提示（仅记录，技术裁定归架构师）**：
- 候选 addon 名（待确认）：`Inventory`（主背包）、`InventoryGrid` / `InventoryLarge`（物品网格）、`InventoryExpansion`（扩充背包）等；需确认用户所指「背包页」对应哪个 addon 实例。
- 通过 `GameGui.GetAddonByName(...)` 取得 `AtkUnitBase*`，在 `AddonLifecycle` 的 `Setup`/`PreDraw` 事件中向目标父节点追加一个按钮 `AtkResNode`（图标 + 点击回调 → `MainWindow.Toggle()`）。
- 按钮回调仅置 `MainWindow.IsOpen = true`（或调用 `plugin.MainWindow.Toggle()`），运行于 UI 线程，安全。

---

## 5. 与现有能力的复用 / 冲突分析

### Feature 1 vs 现有「数量阈值策略」—— **复用为主，不新建丢弃引擎**

经核对 v1.0.1 源码，结论如下：

| 维度 | 现有实现（v1.0.1） | 本次增量需要 | 关系 |
|------|-------------------|--------------|------|
| 策略模型 | `Configuration` 有**全局** `QuantityMode Mode`（DiscardAll / DiscardAboveThreshold / KeepBelowThreshold）+ `int QuantityThreshold` | 需**条目级**阈值 | 新增条目字段，**复用** `QuantityMode.KeepBelowThreshold` 语义 |
| 丢弃执行 | `DiscardExecutor.SplitAndDiscard(container, slot, excess)`：`SplitItem` 拆出 `excess` → 仅丢拆分槽 | 只丢超出部分、不超丢 | **完全复用**，无需新写拆分/丢弃逻辑 |
| 判定入口 | `AutoDiscardService.Process` 读 `config.Mode` / `config.QuantityThreshold`（全局） | 按条目取阈值 | **修改** `Process`：条目 `HasThreshold=true` 时用条目阈值走 KeepBelowThreshold；否则回退全局 |
| 数据模型 | `TrashItemEntry`：仅 `ItemId` / `DisplayName` / `IsFuzzy` | 需条目级阈值 | **新增** `int QuantityThreshold` + `bool HasThreshold` |
| 添加交互 | 「添加」页左键整格点击加项，无阈值输入 | 右键输入阈值 | **新增** 右键弹窗（UI 增量） |
| 导入导出 | `ImportExport` JSON/CSV 序列化 `TrashItemEntry` | 新字段需兼容 | JSON 自动兼容（缺字段默认）；CSV 表头建议加列（P2） |

**结论**：
1. **丢弃「保留阈值、只丢超出」的核心能力已存在**（全局 `KeepBelowThreshold` + `SplitAndDiscard`），且 `SplitAndDiscard` 结构上保证「只丢 excess、不超丢」——本次**直接复用，不新建任何原生调用**。
2. 唯一「新建」的是**条目级数据字段 + 添加页右键输入 UI + Process 按条目解析阈值**；引擎与策略枚举全部复用。
3. 旧列表（无 `HasThreshold`）`HasThreshold=false` → 走全局策略，**行为零回归**。
4. **不冲突**：全局策略保留为「未设条目阈值时的兜底」，二者正交。

**⚠ 关键语义点（务必告知架构师）**：`SplitAndDiscard` 是**按槽位（slot）**计算 `excess = slotQty − threshold`。若同一物品分散在背包多个槽位：
- 安全（不超丢）：每个槽位数量 ≤ 阈值 → 全不动，不会误删。
- 但与用户「只留 N 个（总计）」的心智模型可能有出入：如两个槽位各 12、阈值 10 → 各丢 2 → 实际留 20（不是 10）。
- 是否需在丢弃前**按 ItemId 跨槽位聚合**再决定，交由架构师在待确认问题中裁定（见 §6 Q-F1-3）。

### Feature 2 vs 现有 UI —— **全新能力，无既有代码可复用**

- v1.0.1 主窗口为 **ImGui**（WindowSystem），`/atrash` 经 `Commands` 调用 `MainWindow.Toggle()`；仓库内**无任何 AtkUnitBase 钩子或 KamiToolKit 引用**。
- 历史背景澄清：此前「背包网格仿原生观感」需求被否、改用 ImGui 仿真，指的是**「添加」页的物品网格**用 ImGui 画成游戏观感——与本次「在背包原生界面注入一个按钮」是**两回事**，不冲突，也不构成先例。
- 本次需**新增**原生 UI 注入模块（如 `Windows/NativeInventoryButton.cs`），仅「点击 → Toggle 主窗口」复用现有 `MainWindow.Toggle()`，其余为新增。

---

## 6. 待确认问题（Open Questions）

### 给架构师
- **Q-F1-1（条目字段语义）**：`TrashItemEntry` 新增 `int QuantityThreshold` + `bool HasThreshold` 是否足够？还是应同时存 `QuantityMode`（支持条目级「超阈值整堆丢」）？建议 v1.0.2 仅做「保留 N（KeepBelowThreshold）」一种，模式多样化留 P2。
- **Q-F1-2（Process 改法）**：`AutoDiscardService.Process` 需按 `ItemId` 反查 `TrashListStore` 取条目阈值。是否新增 `TrashListStore.GetEntry(uint itemId)`？反查开销是否可接受（列表通常不大）？
- **Q-F1-3（多槽位聚合）**：同一物品多槽位时，是否按 ItemId 跨槽位聚合后统一「保留 N」？还是维持现状按 slot 独立判定（更保守、绝不超丢，但可能留多于 N）？**本增量建议维持按 slot 判定（安全优先）**，聚合作为后续增强。
- **Q-F1-4（右键输入初值）**：右键弹窗的「保留数量」初值取什么？建议默认 = 该物品当前背包数量（即先不丢），由用户下调。
- **Q-F2-1（注入目标 addon）**：用户所说「背包页」对应哪个 AtkUnitBase 实例（`Inventory` / `InventoryGrid` / `InventoryLarge` / `InventoryExpansion`）？需实测确认父节点与布局锚点。
- **Q-F2-2（注入技术选型）**：单按钮用 **AtkUnitBase 手动钩子** 还是 **KamiToolKit**？用户曾因 KamiToolKit 兼容性风险否决「背包网格仿原生」，但本次仅是**单按钮**，请架构师重新评估 KamiToolKit 风险是否可接受；若不可接受则走手动 AtkUnitBase 节点注入。
- **Q-F2-3（生命周期/清理）**：addon `Setup` 注入、`Finalize` 移除；刷新/重排时防重复注入；插件 `Dispose` 时彻底移除节点，避免泄漏或残留可点击幽灵按钮。
- **Q-F2-4（按钮可见性与状态）**：按钮是否仅在背包可见时出现？是否随插件 `Enabled` 置灰（建议：始终可点开窗口，与 `Enabled` 解耦）？

### 给用户
- **Q-U-1**：条目级阈值默认是否「保留 N 个」即可？是否也需要「超 N 才整堆丢」的条目级模式？
- **Q-U-2**：垃圾桶按钮希望放在背包界面的哪个位置（头部操作区 / 底部 / 侧边）？是否接受注入到主背包即可（扩充背包页是否也需要）？
- **Q-U-3**：是否希望按钮仅在 AutoTrash 启用时显示，还是任何时候都可点开设置？

---

## 7. 发布说明草稿（v1.0.2）

**AutoTrash 自动丢弃垃圾桶 v1.0.2**
- 版本：AssemblyVersion `1.0.2.0`，Release `v1.0.2`，DalamudApiLevel 15。
- 新增功能：
  - **条目级数量阈值**：在「添加」页右键物品可设置「保留数量」，该物品进包超过阈值时只丢超出部分、精确保留阈值数量，绝不超丢；未设置的条目仍按设置页全局数量策略处理（兼容旧行为）。
  - **背包原生垃圾桶按钮**：在游戏背包界面注入原生垃圾桶按钮，点击即打开 AutoTrash 主窗口（等同 `/atrash`）。
- 兼容与注意：
  - 旧配置文件/列表导入无阈值字段时按默认处理，行为不变。
  - JSON 配置向后兼容；旧 CSV 仍可导入（阈值列缺省为 0/未启用）。
  - 背包按钮为原生注入，若与个别 UI 修改类插件布局冲突，可暂时用 `/atrash` 代替。
- 发布动作：更新 `Properties/AssemblyInfo.cs` 与 `auto_trash_can.json` 版本号；打 GitHub Release `v1.0.2`；同步插件源 `pluginmaster.json`（外部仓库 `ijnokmsc/DalamudPlugins`）。

---

> 附：本增量 PRD 不重写既有需求（自动丢弃主流程、HQ 保护、容器白名单、日志、导入导出等仍见 PRD.md / Design.md）。新功能均尽量复用现有引擎与策略枚举，降低回归风险。
