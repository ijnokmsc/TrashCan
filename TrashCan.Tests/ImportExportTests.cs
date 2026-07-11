using AutoTrash.Core;
using AutoTrash.Models;
using Xunit;

namespace AutoTrash.Tests;

public class ImportExportTests
{
    private static List<TrashItemEntry> Sample()
    {
        return new List<TrashItemEntry>
        {
            new(1001, "Hi-Potion", false),
            new(0, "Potion", true),              // 模糊匹配条目
            new(2002, "A,B weird name", false),  // 含逗号，需转义
            new(3003, "He said \"hi\"", false),  // 含引号，需转义
        };
    }

    [Fact]
    public void Json_RoundTrip_PreservesAllFields()
    {
        var list = Sample();
        var json = ImportExport.ExportJson(list);
        var back = ImportExport.ImportJson(json);
        Assert.Equal(list.Count, back.Count);
        for (int i = 0; i < list.Count; i++)
        {
            Assert.Equal(list[i].ItemId, back[i].ItemId);
            Assert.Equal(list[i].DisplayName, back[i].DisplayName);
            Assert.Equal(list[i].IsFuzzy, back[i].IsFuzzy);
        }
    }

    [Fact]
    public void Csv_RoundTrip_PreservesAllFields_IncludingCommaAndQuote()
    {
        var list = Sample();
        var csv = ImportExport.ExportCsv(list);
        var back = ImportExport.ImportCsv(csv);
        Assert.Equal(list.Count, back.Count);
        for (int i = 0; i < list.Count; i++)
        {
            Assert.Equal(list[i].ItemId, back[i].ItemId);
            Assert.Equal(list[i].DisplayName, back[i].DisplayName);
            Assert.Equal(list[i].IsFuzzy, back[i].IsFuzzy);
        }
    }

    [Fact]
    public void Csv_QuotedNameWithComma_ParsesBack()
    {
        const string name = "A,B";
        var csv = ImportExport.ExportCsv(new List<TrashItemEntry> { new(7, name, false) });
        var back = ImportExport.ImportCsv(csv);
        Assert.Single(back);
        Assert.Equal("A,B", back[0].DisplayName);
    }

    [Fact]
    public void Csv_QuotedNameWithQuote_ParsesBack()
    {
        const string name = "He said \"hi\"";
        var csv = ImportExport.ExportCsv(new List<TrashItemEntry> { new(8, name, false) });
        var back = ImportExport.ImportCsv(csv);
        Assert.Single(back);
        Assert.Equal(name, back[0].DisplayName);
    }

    [Fact]
    public void ImportAuto_DetectsJsonVsCsv()
    {
        var json = ImportExport.ExportJson(Sample());
        var csv = ImportExport.ExportCsv(Sample());
        Assert.Equal(Sample().Count, ImportExport.ImportAuto(json).Count);
        Assert.Equal(Sample().Count, ImportExport.ImportAuto(csv).Count);
    }

    [Fact]
    public void Import_NullOrEmpty_ReturnsEmpty_NoThrow()
    {
        Assert.Empty(ImportExport.ImportJson(null!));
        Assert.Empty(ImportExport.ImportJson(""));
        Assert.Empty(ImportExport.ImportCsv(null!));
        Assert.Empty(ImportExport.ImportCsv(""));
    }

    [Fact]
    public void Import_MalformedJson_ReturnsEmpty_NoThrow()
    {
        Assert.Empty(ImportExport.ImportJson("{ not valid json"));
    }
}
