using Dalamud.Game.Command;
using AutoTrash.Windows;

namespace AutoTrash.Windows;

/// <summary>
/// /atrash 命令，用于开关主窗口。
/// </summary>
public class Commands
{
    private const string CommandName = "/atrash";
    private readonly Plugin plugin;

    public Commands(Plugin plugin)
    {
        this.plugin = plugin;
    }

    /// <summary>注册命令处理器。</summary>
    public void Register()
    {
        Plugin.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "打开 AutoTrash 自动丢弃垃圾桶主窗口（可重复执行以开关）",
            ShowInHelp = true,
        });
    }

    /// <summary>注销命令处理器。</summary>
    public void Unregister()
    {
        Plugin.CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string arguments)
    {
        plugin.MainWindow.Toggle();
    }
}
