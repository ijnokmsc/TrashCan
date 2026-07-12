using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace AutoTrash.Windows;

/// <summary>
/// 共享的 ImGui 辅助方法。
/// </summary>
public static class UiHelpers
{
    /// <summary>带标题的辅助文本（灰色小字）。</summary>
    public static void Hint(string text)
    {
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), text);
    }

    /// <summary>带标题的辅助文本（灰色小字，自动换行，避免长文本超出窗口被截断）。</summary>
    public static void HintWrapped(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    /// <summary>状态消息（琥珀色）。</summary>
    public static void Status(string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.4f, 1f), text);
        }
    }

    /// <summary>错误/失败消息（红色）。</summary>
    public static void Error(string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), text);
        }
    }

    /// <summary>成功消息（绿色）。</summary>
    public static void Success(string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            ImGui.TextColored(new Vector4(0.4f, 1f, 0.5f, 1f), text);
        }
    }

    /// <summary>信息问号标记（hover 显示说明）。</summary>
    public static void HelpMarker(string text)
    {
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35f);
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }
}
