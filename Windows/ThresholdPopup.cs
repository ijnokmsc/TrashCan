using System.Numerics;
using Dalamud.Bindings.ImGui;
using AutoTrash.Services;

namespace AutoTrash.Windows;

/// <summary>
/// 条目级「保留数量」弹窗（添加页 / 列表页共用）。
/// 右键物品格子时由 MainWindow 调用 <see cref="Prepare"/> 初始化状态并 OpenPopup，
/// 随后在 MainWindow 主绘制循环里调用 <see cref="Draw"/> 渲染，并把结果直接写入 <see cref="TrashListStore"/>。
///
/// 语义：启用阈值后强制「保留 N、仅丢超出 N 的部分」（条目级，独立于全局数量策略）；
/// 取消启用或点击「清除阈值」则回退到全局策略（HasThreshold=false）。
/// </summary>
public sealed class ThresholdPopup
{
    /// <summary>ImGui 弹窗 ID（MainWindow 用其 OpenPopup / BeginPopup）。</summary>
    public const string PopupId = "autotrash_threshold_popup";

    private uint itemId;
    private string itemName = string.Empty;
    private int threshold;
    private bool enabled;

    /// <summary>右键触发时初始化弹窗状态。</summary>
    /// <param name="itemId">目标物品 ItemId。</param>
    /// <param name="itemName">展示名（可为空）。</param>
    /// <param name="currentQty">初值：添加页=当前背包数量；列表页=已设阈值或 0。</param>
    /// <param name="enabled">是否默认启用阈值：添加页=true（先不丢，由用户下调）；列表页=条目 HasThreshold。</param>
    public void Prepare(uint itemId, string itemName, int currentQty, bool enabled)
    {
        this.itemId = itemId;
        this.itemName = itemName ?? string.Empty;
        this.threshold = currentQty < 0 ? 0 : currentQty;
        this.enabled = enabled;
    }

    /// <summary>
    /// 渲染弹窗内容（假定调用方已完成 BeginPopup / 将负责 EndPopup）。
    /// 由 MainWindow 在右键上下文弹窗（BeginPopupContextItem）内调用；用户点击「应用 / 清除阈值」
    /// 时把结果写入 <paramref name="listStore"/> 并返回状态消息，否则返回 null。
    /// </summary>
    /// <param name="listStore">待写入的列表存储。</param>
    /// <returns>操作状态消息（供主窗口琥珀色展示）；无操作返回 null。</returns>
    public string? DrawContent(TrashListStore listStore)
    {
        string? status = null;

        ImGui.Text($"设置保留数量：{itemName}");
        ImGui.Separator();

        // 仅切换开关，提交由下方「应用」按钮执行
        if (ImGui.Checkbox("启用保留数量（仅丢超出部分）", ref enabled))
        {
            // 状态已在 enabled 中，无需额外处理
        }

        ImGui.PushItemWidth(160f);
        if (ImGui.InputInt("保留数量 N", ref threshold))
        {
            if (threshold < 0)
            {
                threshold = 0;
            }
        }

        ImGui.PopItemWidth();
        UiHelpers.HelpMarker("保留 N 个，仅丢超出 N 的部分（条目级阈值，独立于全局数量策略）。");

        ImGui.Separator();

        if (ImGui.Button("应用", new Vector2(120, 0)))
        {
            var t = threshold < 0 ? 0 : threshold;
            if (enabled)
            {
                listStore.UpdateThreshold(itemId, itemName, t);
                status = $"已为「{itemName}」设置保留数量 {t}";
            }
            else
            {
                listStore.ClearThreshold(itemId, itemName);
                status = $"已为「{itemName}」关闭保留数量（回退全局策略）";
            }

            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();

        if (ImGui.Button("清除阈值", new Vector2(120, 0)))
        {
            listStore.ClearThreshold(itemId, itemName);
            status = $"已清除「{itemName}」的保留数量";
            ImGui.CloseCurrentPopup();
        }

        return status;
    }
}
