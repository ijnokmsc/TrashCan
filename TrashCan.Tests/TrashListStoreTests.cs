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
}
