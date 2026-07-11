using System.Collections.Generic;
using AutoTrash.Models;

namespace AutoTrash.Services;

/// <summary>
/// 丢弃日志的追加 / 读取 / 清空，底层使用 Configuration.DiscardLog，
/// 并依据 Configuration.LogCap 做滚动截断。
/// </summary>
public class LogStore
{
    private readonly Configuration config;

    public LogStore(Configuration config)
    {
        this.config = config;
    }

    /// <summary>当前日志（只读视图，旧 -> 新）。</summary>
    public IReadOnlyList<DiscardLogEntry> Entries => config.DiscardLog;

    /// <summary>追加一条日志，并滚动截断到 LogCap。</summary>
    public void Append(DiscardLogEntry entry)
    {
        if (entry == null)
        {
            return;
        }

        config.DiscardLog.Add(entry);

        var cap = config.LogCap > 0 ? config.LogCap : 500;
        while (config.DiscardLog.Count > cap)
        {
            config.DiscardLog.RemoveAt(0);
        }

        config.Save();
    }

    /// <summary>清空日志。</summary>
    public void Clear()
    {
        config.DiscardLog.Clear();
        config.Save();
    }
}
