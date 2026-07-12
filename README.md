# AutoTrash

FF14（最终幻想XIV）Dalamud 插件 —— 按物品列表自动清理背包杂物。面向国服 XIVLauncherCN。

## 功能

- **仿游戏背包网格**：在“添加”页点击背包物品加入丢弃列表，在“列表”页点击整格移除
- **三种网格密度**：紧凑 / 标准 / 宽松，适配不同屏幕
- **仅扫描背包 1~4**：陆行鸟鞍囊、部队存储柜、军武库等其他容器均受保护，不会被误丢
- **HQ 保护**：默认不丢 HQ 物品，可手动开启
- **数量阈值策略**：整堆丢弃 / 超阈值整堆丢弃 / 保留阈值以下（仅丢超出部分）
- **手动 / 定时扫描**：物品获得事件仅记录，到扫描时刻才执行丢弃；支持定时自动或手动“立即扫描”
- **首次使用警告**：首次打开插件弹出风险提示，确认后不再弹出
- **安全编辑**：主窗口打开期间暂停自动删除，防止误加物品被立即删除；关闭窗口后执行一次扫描删除
- **列表导入导出**：支持 JSON / CSV 导入导出（文本框与文件）
- **丢弃日志**：仅记录实际成功丢弃的物品

## 运行环境

| 依赖 | 版本 |
|------|------|
| Dalamud API | 15 |
| .NET | 10 |
| 游戏客户端 | 国服 XIVLauncherCN |

## 编译

```bash
git clone https://github.com/ijnokmsc/TrashCan.git
cd TrashCan
dotnet build
```

编译产物自动部署到：

```
C:\Users\你的用户名\AppData\Roaming\XIVLauncherCN\devPlugins\TrashCan\
```

## 安装到游戏

### 方式一：插件仓库（推荐）

1. 打开游戏，输入 `/xlsettings`
2. 左侧菜单 → **插件仓库** → **添加仓库**
3. 粘贴以下地址：
   ```
   https://raw.githubusercontent.com/ijnokmsc/DalamudPlugins/main/pluginmaster.json
   ```
4. 确定 → 重新整理插件列表
5. 搜索 **AutoTrash** → 安装

### 方式二：手动安装

1. 从 [Releases](https://github.com/ijnokmsc/TrashCan/releases) 下载 `latest.zip`
2. 解压到 `%APPDATA%\XIVLauncherCN\devPlugins\TrashCan\`
3. 游戏内 `/xlplugins` → 开发者插件 → 刷新并启用

## 使用命令

| 命令 | 说明 |
|------|------|
| `/atrash` | 打开/关闭主窗口 |

## 许可证

MIT
