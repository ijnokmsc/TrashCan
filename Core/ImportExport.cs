using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using AutoTrash.Models;

namespace AutoTrash.Core;

/// <summary>
/// 待丢弃列表的 JSON / CSV 导入导出。
/// </summary>
public static class ImportExport
{
    /// <summary>导出为格式化 JSON 字符串。</summary>
    public static string ExportJson(List<TrashItemEntry> list)
    {
        return JsonConvert.SerializeObject(list ?? new List<TrashItemEntry>(), Formatting.Indented);
    }

    /// <summary>
    /// 从 JSON 字符串导入；解析失败返回空列表（不抛异常）。
    /// </summary>
    public static List<TrashItemEntry> ImportJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<TrashItemEntry>();
        }

        try
        {
            var list = JsonConvert.DeserializeObject<List<TrashItemEntry>>(json);
            return list ?? new List<TrashItemEntry>();
        }
        catch
        {
            return new List<TrashItemEntry>();
        }
    }

    /// <summary>导出为 CSV 字符串（表头：ItemId,DisplayName,IsFuzzy）。</summary>
    public static string ExportCsv(List<TrashItemEntry> list)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ItemId,DisplayName,IsFuzzy");
        if (list != null)
        {
            foreach (var e in list)
            {
                var name = (e.DisplayName ?? string.Empty).Replace("\"", "\"\"");
                sb.AppendLine($"{e.ItemId},\"{name}\",{e.IsFuzzy}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 从 CSV 字符串导入；解析失败返回已成功解析的部分（不抛异常）。
    /// 支持带引号、含逗号的 DisplayName。
    /// </summary>
    public static List<TrashItemEntry> ImportCsv(string csv)
    {
        var result = new List<TrashItemEntry>();
        if (string.IsNullOrWhiteSpace(csv))
        {
            return result;
        }

        var lines = csv.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            // 跳过表头
            if (i == 0 && line.StartsWith("ItemId", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var cells = ParseCsvLine(line);
            if (cells.Count < 2)
            {
                continue;
            }

            var itemId = uint.TryParse(cells[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0;
            var name = cells[1].Trim();
            var fuzzy = cells.Count >= 3 && bool.TryParse(cells[2].Trim(), out var f) && f;

            if (itemId == 0 && string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            result.Add(new TrashItemEntry(itemId, name, fuzzy));
        }

        return result;
    }

    /// <summary>尝试 JSON 优先，失败回退 CSV，从文本导入。</summary>
    public static List<TrashItemEntry> ImportAuto(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<TrashItemEntry>();
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith("[") || trimmed.StartsWith("{"))
        {
            return ImportJson(text);
        }

        return ImportCsv(text);
    }

    /// <summary>将文本写入文件（用于“导出到文件”）。</summary>
    public static void WriteFile(string path, string content)
    {
        File.WriteAllText(path, content);
    }

    /// <summary>从文件读取文本（用于“从文件导入”）。</summary>
    public static string ReadFile(string path)
    {
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    /// <summary>解析一行 CSV，正确处理引号与转义逗号。</summary>
    private static List<string> ParseCsvLine(string line)
    {
        var cells = new List<string>();
        var inQuotes = false;
        var cur = new StringBuilder();

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        cur.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    cur.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    cells.Add(cur.ToString());
                    cur.Clear();
                }
                else
                {
                    cur.Append(c);
                }
            }
        }

        cells.Add(cur.ToString());
        return cells;
    }
}
