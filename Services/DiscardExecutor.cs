using FFXIVClientStructs.FFXIV.Client.Game;

namespace AutoTrash.Services;

/// <summary>
/// 封装原生 InventoryManager 调用。
/// 所有方法必须在游戏主线程（IFramework.Update 或 RunOnFrameworkThread）内调用。
/// </summary>
public unsafe class DiscardExecutor
{
    /// <summary>
    /// 整堆丢弃。返回原生返回值（0 表示成功）。
    /// </summary>
    public virtual int Discard(InventoryType container, ushort slot)
    {
        var im = InventoryManager.Instance();
        if (im == null)
        {
            return -1;
        }

        return im->DiscardItem(container, slot);
    }

    /// <summary>
    /// 拆分并丢弃 excess 个，保留剩余在背包。
    /// SplitItem 返回新槽位（&gt;0 表示成功），随后对新槽位调用 DiscardItem。
    /// 若拆分失败（返回 &lt;=0），返回该错误码。
    /// </summary>
    public virtual int SplitAndDiscard(InventoryType container, ushort slot, int excess)
    {
        if (excess <= 0)
        {
            return Discard(container, slot);
        }

        var im = InventoryManager.Instance();
        if (im == null)
        {
            return -1;
        }

        var newSlot = im->SplitItem(container, slot, excess);
        if (newSlot <= 0)
        {
            return newSlot;
        }

        return im->DiscardItem(container, (ushort)newSlot);
    }
}
