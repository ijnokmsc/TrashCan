# AutoTrash

FF14（最终幻想14）Dalamud 插件 —— 按物品列表自动清理背包杂物。

## 功能
- 根据配置列表自动丢弃背包（Inventory1~4）中的物品
- 支持精确 ItemId 与模糊名称匹配
- HQ 保护 / 数量阈值策略
- 列表 JSON / CSV 导入导出
- 丢弃日志

## 使用
- 命令：`/atrash` 打开主窗口
- 首次打开会提示风险警告，确认后即可使用
- 打开主窗口期间暂停自动删除，关闭后执行一次扫描删除

## 安装
通过 XIVLauncherCN 自定义插件源（DalamudPlugins）安装；或手动将 Release 中的 `latest.zip` 解压到 `devPlugins/TrashCan/`。

## 许可证
MIT
