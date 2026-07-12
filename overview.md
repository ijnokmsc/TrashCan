# AutoTrash 增量迭代总结

## 已完成的多轮改动
1. **列表页改为仿游戏背包网格**：与添加页一致的三模式、整格点击移除、图标/数量角标/悬停 tooltip。
2. **关闭窗口触发一次扫描**：`MainWindow` 关闭时安全调用 `InventoryWatcher.TriggerScan()`。
3. **日志只记录成功丢弃**：`AutoDiscardService.Process()` 中跳过/失败分支不再写日志。
4. **清空列表显式保存**：清空按钮后调用 `config.Save()`。
5. **白名单只保留背包 1-4**：移除鞍袋/高级鞍袋容器，删除 `ProtectSaddlebag` 配置及开关。
6. **设置界面压缩**：相关选项并到同一行。
7. **列表页删除「清理不在背包的条目」按钮**。
8. **「清空列表」增加确认弹窗**：点击后弹出 ImGui 模态确认窗，确认才清空。
9. **设置页 UI 比例修复**：限制 InputInt / Combo 宽度为 120，避免同一行控件被挤下；底部说明文字改用 `HintWrapped` 自动换行，避免截断。
10. **主动扫描修复**：`TriggerScan()` 不再只处理 `pendingItems`，改为两阶段：先处理事件缓存，再主动遍历背包 1~4，将列表命中物品入队丢弃。关闭窗口与「立即扫描」现在都会真正丢弃背包内列表物品。
11. **文案校对**：UI 中「鞍袋」改为「陆行鸟鞍囊」，「公司仓库」改为「部队存储柜」；`auto_trash_can.json` 描述改为「背包（Inventory1~4）」。
12. **首次打开警告弹窗**：`Configuration.HasShownWarning` 持久化标记，主窗口第一次打开时弹模态警告「删除后无法找回，谨慎添加」，确认后不再弹。
13. **打开插件暂停自动删除**：主窗口打开期间 `AutoDiscardService.Paused = true`，停止所有自动/定时/扫描丢弃；关闭窗口瞬间先 `Paused = false` 再触发扫描，确保关闭后才执行丢弃。

## 五 Bug 修复轮（2026-07-12）
1. **Bug 1 丢弃失效**：原 `Paused=false` 复位写在 `MainWindow.Draw()` 的关闭检测分支，但 Dalamud `WindowSystem` 对已关闭窗口不调用 `Draw()`，导致点 X 后 `Paused` 永久卡 `true`、自动丢弃全失效。改为 `OnOpen()` 置 `Paused=true`、`OnClose()` 置 `Paused=false` + `TriggerScanOnClose()`，从 `Draw()` 移除脆弱的关闭检测。
2. **Bug 2 两无堆叠只丢一个**：`PendingDiscard` 记录的槽位在丢一件后背包压缩而失效。新增 `ResolveCurrentSlot(container, itemId, isHq, fallbackSlot)`，丢弃前按 ItemId+IsHq 重新定位真实槽位；安全回退：容器可读但目标已不在 → 跳过（下次扫描自愈，防误丢）；容器读取异常 → 回退原槽位。
3. **Bug 3 标题无版本**：主窗口标题改为 `AutoTrash 自动丢弃垃圾桶 v{Major}.{Minor}.{Build}`（参考 CraftFlow `GetVersionString()`）。
4. **Bug 4 README 不准确**：整体重写为准确功能/运行环境/编译/安装/命令说明。
5. **Bug 5 DalamudPlugins README 未更新**：插件列表表追加 AutoTrash 行（1.0.0.0），插件源码段追加链接。

## 公开发布
- 公共仓库：https://github.com/ijnokmsc/TrashCan
- 首次 Release：https://github.com/ijnokmsc/TrashCan/releases/tag/v1.0.0
- 自定义插件源：https://github.com/ijnokmsc/DalamudPlugins (pluginmaster.json 已追加 AutoTrash 条目)
- 图标文件：DalamudPlugins/icons/trashcan.png
- 版本：1.0.0.0，DalamudApiLevel 15
- 下载链路实测：200 OK

## v1.0.1 修复（版本号显示 0.0.0 + 补发前 5 项 Bug 修复）
- 根因：`csproj` 的 `<GenerateAssemblyInfo>false</GenerateAssemblyInfo>` 使 `<AssemblyVersion>1.0.0.0</AssemblyVersion>` 属性失效，DLL 版本回退 0.0.0.0 → 窗口标题显示 `v0.0.0`、清单 `AssemblyVersion` 为 0.0.0.0。
- 修复：新增 `Properties/AssemblyInfo.cs` 手动声明 `[assembly: AssemblyVersion("1.0.1.0")]`（Dalamud SDK 不注入 AssemblyVersion，无 CS0579 冲突）。
- 版本 bump 至 1.0.1.0；此版本同时包含此前未发布的 5 项 Bug 修复（Paused 失效、槽位重定位、标题版本、两份 README）。
- Release：https://github.com/ijnokmsc/TrashCan/releases/tag/v1.0.1 ；插件源 `pluginmaster.json` 已同步版本与下载链接（v1.0.1）。
- 打包注意：`bin/Release/auto_trash_can/` 是 Dalamud SDK 生成的嵌套子目录（含旧 latest.zip 残留），发布包应只取根目录 `auto_trash_can.dll` + `auto_trash_can.json` + `auto_trash_can.deps.json`，勿递归打包整个 bin/Release。

## 验证结果
- 构建：`dotnet build -c Release` 0 错误 / 0 警告
- DLL 版本：1.0.1.0（bin/Release 与 devPlugins 均确认）
- 测试：`dotnet test` 83 / 83 通过
- Release 下载链路：200 OK

## 涉及文件
- `Core/Constants.cs`
- `Configuration.cs`
- `Windows/MainWindow.cs`
- `Services/AutoDiscardService.cs`
- `Services/InventoryWatcher.cs`
- `Plugin.cs`
- `auto_trash_can.json`
- `TrashCan.Tests/ConstantsTests.cs`
- `TrashCan.Tests/ConfigurationTests.cs`
- `TrashCan.Tests/QuantityStrategyTests.cs`
- 以及前序轮次修改的 `ListGateTests.cs` / `SpecialItemProtectionTests.cs`

## 遗留建议
- 首次警告弹窗与暂停逻辑已加，建议实机点检：首次打开弹窗、打开期间不删、关闭后才删。
- `Design.md` 与 `class-diagram.mermaid` 仍描述旧设计（鞍袋白名单 / `ProtectSaddlebag`），建议后续同步。
