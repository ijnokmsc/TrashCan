using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.Gui;
using AutoTrash.Core;
using AutoTrash.Services;
using AutoTrash.Windows;

namespace AutoTrash;

/// <summary>
/// AutoTrash 插件入口。
/// 通过 [PluginService] 静态属性注入 Dalamud 服务，在构造函数中装配各 Service 与 Window。
/// </summary>
public sealed class Plugin : IDalamudPlugin, IDisposable
{
    /// <summary>插件接口（配置持久化 / UI 绘制）。</summary>
    [PluginService] public static IDalamudPluginInterface PluginInterface { get; set; } = null!;

    /// <summary>客户端状态（登录判定）。</summary>
    [PluginService] public static IClientState ClientState { get; set; } = null!;

    /// <summary>背包变更事件源。</summary>
    [PluginService] public static IGameInventory GameInventory { get; set; } = null!;

    /// <summary>框架线程（原生调用必须在 Framework.Update 内）。</summary>
    [PluginService] public static IFramework Framework { get; set; } = null!;

    /// <summary>游戏数据（Lumina 物品解析）。</summary>
    [PluginService] public static IDataManager DataManager { get; set; } = null!;

    /// <summary>命令管理器（/atrash）。</summary>
    [PluginService] public static ICommandManager CommandManager { get; set; } = null!;

    /// <summary>纹理提供器（用于加载物品游戏图标）。</summary>
    [PluginService] public static ITextureProvider TextureProvider { get; set; } = null!;

    /// <summary>原生 addon 生命周期服务：监听背包 addon 的 PostSetup / PreFinalize 以注入 / 摘除垃圾桶按钮。</summary>
    [PluginService] public static IAddonLifecycle AddonLifecycle { get; set; } = null!;

    /// <summary>原生 addon 事件管理器：为注入的垃圾桶按钮注册 MouseClick 原生事件。</summary>
    [PluginService] public static IAddonEventManager AddonEventManager { get; set; } = null!;

    /// <summary>游戏 GUI 服务：用于在插件加载时若背包已处于打开状态，主动注入垃圾桶按钮（PostSetup 不会再触发）。</summary>
    [PluginService] public static IGameGui GameGui { get; set; } = null!;

    /// <summary>插件日志服务：用于输出原生按钮注入/摘除的诊断信息，便于实机排查。</summary>
    [PluginService] public static IPluginLog PluginLog { get; set; } = null!;

    public Configuration Configuration { get; }
    public TrashListStore TrashListStore { get; }
    public LogStore LogStore { get; }
    public ItemResolver ItemResolver { get; }
    public DiscardExecutor DiscardExecutor { get; }
    public AutoDiscardService AutoDiscardService { get; }
    public InventoryWatcher InventoryWatcher { get; }
    public MainWindow MainWindow { get; }
    public Commands Commands { get; }
    public InventoryTrashButton? TrashButton { get; }
    public WindowSystem WindowSystem { get; } = new();

    public Plugin()
    {
        // 加载或新建配置
        var loaded = PluginInterface.GetPluginConfig();
        Configuration = loaded as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);

        // 装配服务
        TrashListStore = new TrashListStore(Configuration);
        LogStore = new LogStore(Configuration);
        ItemResolver = new ItemResolver(DataManager);
        DiscardExecutor = new DiscardExecutor();
        AutoDiscardService = new AutoDiscardService(Configuration, DiscardExecutor, LogStore, ClientState, Framework, TrashListStore, ItemResolver, GameInventory);
        InventoryWatcher = new InventoryWatcher(GameInventory, Framework, ClientState, Configuration, TrashListStore, ItemResolver);
        MainWindow = new MainWindow(this);
        Commands = new Commands(this);

        // 背包原生垃圾桶按钮：注入 + 生命周期（与 Enabled 解耦）
        TrashButton = new InventoryTrashButton(this, AddonLifecycle, AddonEventManager);
        TrashButton.Enable();

        // 背包监控 -> 自动丢弃队列
        InventoryWatcher.ItemPending += entry => AutoDiscardService.Enqueue(entry);

        // 启用监控与命令
        InventoryWatcher.Enable();
        Commands.Register();

        // 窗口系统
        WindowSystem.AddWindow(MainWindow);

        // 注册 UiBuilder 主界面/配置入口（修复 Config UI / Main UI 未注册警告）
        PluginInterface.UiBuilder.OpenMainUi += OpenMainUiHandler;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUiHandler;

        // UI 绘制
        PluginInterface.UiBuilder.Draw += Draw;
    }

    private void Draw()
    {
        WindowSystem.Draw();
        TrashButton?.Draw();
    }

    /// <summary>UiBuilder.OpenMainUi 处理器：主窗口同时是主 UI 与配置入口，直接打开即可。</summary>
    private void OpenMainUiHandler() => MainWindow.IsOpen = true;

    /// <summary>UiBuilder.OpenConfigUi 处理器：配置入口即主窗口，直接打开即可。</summary>
    private void OpenConfigUiHandler() => MainWindow.IsOpen = true;

    public void Dispose()
    {
        // 先取消 UI 入口订阅，再取消绘制订阅
        PluginInterface.UiBuilder.OpenMainUi -= OpenMainUiHandler;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUiHandler;
        PluginInterface.UiBuilder.Draw -= Draw;
        Commands.Unregister();
        InventoryWatcher.Disable();
        AutoDiscardService.Dispose();
        TrashButton?.Disable();
        TrashButton?.Dispose();
        WindowSystem.RemoveAllWindows();
    }
}
