using AutoTrash.Models;
using AutoTrash.Services;
using Xunit;

namespace AutoTrash.Tests;

public class TrashListStoreTests
{
    // Configuration 未在测试中 Initialize，pluginInterface 为 null，Save() 为 no-op（不触碰 Dalamud）。
    private static TrashListStore NewStore() => new(new Configuration());

    [Fact]
    public void Add_DedupByItemId()
    {
        var s = NewStore();
        s.Add(new TrashItemEntry(1000, "Apple", false));
        s.Add(new TrashItemEntry(1000, "DifferentName", false)); // 同 ItemId，应去重
        Assert.Single(s.Items);
    }

    [Fact]
    public void Add_DedupByDisplayName_CaseInsensitive()
    {
        var s = NewStore();
        s.Add(new TrashItemEntry(0, "Apple", true));
        s.Add(new TrashItemEntry(0, "apple", true)); // 同名（忽略大小写），应去重
        Assert.Single(s.Items);
    }

    [Fact]
    public void Add_DifferentItems_BothKept()
    {
        var s = NewStore();
        s.Add(new TrashItemEntry(1, "A", false));
        s.Add(new TrashItemEntry(2, "B", false));
        Assert.Equal(2, s.Items.Count);
    }

    [Fact]
    public void Add_Null_Ignored()
    {
        var s = NewStore();
        s.Add(null!);
        Assert.Empty(s.Items);
    }

    [Fact]
    public void Remove_ByItemId()
    {
        var s = NewStore();
        s.Add(new TrashItemEntry(100, "X", false));
        s.Remove(new TrashItemEntry(100, "", false));
        Assert.Empty(s.Items);
    }

    [Fact]
    public void Remove_ByDisplayName()
    {
        var s = NewStore();
        s.Add(new TrashItemEntry(0, "Cherry", true));
        s.Remove(new TrashItemEntry(0, "cherry", true));
        Assert.Empty(s.Items);
    }

    [Fact]
    public void Contains_ExactItemId()
    {
        var s = NewStore();
        s.Add(new TrashItemEntry(200, "X", false));
        Assert.True(s.Contains(200, null));
        Assert.False(s.Contains(999, null));
    }

    [Fact]
    public void Contains_FuzzySubstring()
    {
        var s = NewStore();
        s.Add(new TrashItemEntry(0, "Potion", true));
        Assert.True(s.Contains(0, "Super Potion")); // 子串命中
        Assert.False(s.Contains(0, "Nothing"));     // 未命中
    }

    [Fact]
    public void ReplaceAll_ReplacesList()
    {
        var s = NewStore();
        s.Add(new TrashItemEntry(1, "old", false));
        s.ReplaceAll(new List<TrashItemEntry> { new(2, "new", false), new(3, "new2", false) });
        Assert.Equal(2, s.Items.Count);
        Assert.True(s.Contains(2, null));
    }

    [Fact]
    public void GetEntry_ExactItemIdTakesPriorityOverFuzzy()
    {
        var s = NewStore();
        s.Add(new TrashItemEntry(200, "Exact", false));
        s.Add(new TrashItemEntry(0, "Potion", true)); // 模糊
        // 精确 ItemId 命中优先于模糊子串回退
        var e = s.GetEntry(200, "Super Potion");
        Assert.NotNull(e);
        Assert.Equal(200u, e!.ItemId);
    }

    [Fact]
    public void GetEntry_FuzzyFallback_WhenNoExactMatch()
    {
        var s = NewStore();
        s.Add(new TrashItemEntry(0, "Potion", true));
        // 无精确 ItemId 命中，按名称子串回退
        var e = s.GetEntry(555, "Super Potion");
        Assert.NotNull(e);
        Assert.True(e!.IsFuzzy);
    }

    [Fact]
    public void GetEntry_NoMatch_ReturnsNull()
    {
        var s = NewStore();
        s.Add(new TrashItemEntry(0, "Potion", true));
        Assert.Null(s.GetEntry(999, "Nothing"));
    }

    [Fact]
    public void Update_ChangesThresholdFields()
    {
        var s = NewStore();
        s.Add(new TrashItemEntry(300, "X", false));
        s.Update(new TrashItemEntry(300, "X", false) { HasThreshold = true, QuantityThreshold = 9 });
        var e = s.GetEntry(300, null);
        Assert.NotNull(e);
        Assert.True(e!.HasThreshold);
        Assert.Equal(9, e.QuantityThreshold);
    }

    [Fact]
    public void ClearThreshold_RemovesThresholdButKeepsEntry()
    {
        var s = NewStore();
        s.Add(new TrashItemEntry(400, "Y", false) { HasThreshold = true, QuantityThreshold = 4 });
        s.ClearThreshold(400, "Y");
        var e = s.GetEntry(400, null);
        Assert.NotNull(e);
        Assert.False(e!.HasThreshold); // 阈值清除
        Assert.True(s.Contains(400, null)); // 仍在列表中
    }

    [Fact]
    public void UpdateThreshold_AddsNewEntryWhenMissing()
    {
        var s = NewStore();
        s.UpdateThreshold(500, "Z", 6);
        var e = s.GetEntry(500, "Z");
        Assert.NotNull(e);
        Assert.True(e!.HasThreshold);
        Assert.Equal(6, e.QuantityThreshold);
        Assert.True(s.Contains(500, "Z"));
    }
}
