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

## 验证结果
- 构建：`dotnet build` 0 错误 / 0 警告
- 测试：`dotnet test` 82 / 82 通过

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
