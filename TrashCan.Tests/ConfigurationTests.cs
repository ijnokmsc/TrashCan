using AutoTrash;
using AutoTrash.Core;
using AutoTrash.Models;
using Newtonsoft.Json;
using Xunit;

namespace AutoTrash.Tests;

public class ConfigurationTests
{
    [Fact]
    public void Configuration_SerializationRoundTrip_PreservesFields()
    {
        var cfg = new Configuration
        {
            Version = 1,
            Enabled = false,
            DiscardHq = true,
            Mode = QuantityMode.KeepBelowThreshold,
            QuantityThreshold = 42,
            LogCap = 123,
        };
        cfg.TrashList.Add(new TrashItemEntry(100, "Apple", false));
        cfg.TrashList.Add(new TrashItemEntry(0, "Potion", true));
        var logTime = DateTime.Parse("2025-01-02T03:04:05");
        cfg.DiscardLog.Add(new DiscardLogEntry(logTime, 100, "Apple", 5, 0, true, "ok"));

        // 使用与插件一致的 Newtonsoft.Json 进行往返
        var json = JsonConvert.SerializeObject(cfg, Formatting.Indented);
        var back = JsonConvert.DeserializeObject<Configuration>(json)!;

        Assert.Equal(1, back.Version);
        Assert.False(back.Enabled);
        Assert.True(back.DiscardHq);
        Assert.Equal(QuantityMode.KeepBelowThreshold, back.Mode);
        Assert.Equal(42, back.QuantityThreshold);
        Assert.Equal(123, back.LogCap);

        Assert.Equal(2, back.TrashList.Count);
        Assert.Equal(100u, back.TrashList[0].ItemId);
        Assert.Equal("Apple", back.TrashList[0].DisplayName);
        Assert.False(back.TrashList[0].IsFuzzy);
        Assert.True(back.TrashList[1].IsFuzzy);

        Assert.Single(back.DiscardLog);
        Assert.Equal(logTime, back.DiscardLog[0].Time);
        Assert.Equal(100u, back.DiscardLog[0].ItemId);
        Assert.Equal("ok", back.DiscardLog[0].Note);
        Assert.True(back.DiscardLog[0].Success);
    }

    [Fact]
    public void Configuration_PluginInterface_NotSerialized()
    {
        var cfg = new Configuration();
        var json = JsonConvert.SerializeObject(cfg);
        // pluginInterface 标 [JsonIgnore] 且为 private field，序列化结果不应包含该字段
        Assert.DoesNotContain("pluginInterface", json, System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 增量回归：反序列化一份“不含” ProtectSpecialItems / AllowDiscardEquip 字段的旧版 JSON 时，
    /// 两字段应取默认值（true / false），不破坏既有用户配置。
    /// </summary>
    [Fact]
    public void Configuration_OldJsonWithoutNewFields_UsesDefaults()
    {
        // 旧版配置 JSON：没有 ProtectSpecialItems / AllowDiscardEquip 字段
        const string oldJson = """
        {
          "Version": 1,
          "Enabled": true,
          "DiscardHq": false,
          "Mode": 0,
          "QuantityThreshold": 0,
          "TrashList": [],
          "DiscardLog": [],
          "LogCap": 500
        }
        """;

        var cfg = JsonConvert.DeserializeObject<Configuration>(oldJson)!;

        // 既有的增量默认值：总开关默认开（保护），默认不允许丢装备
        Assert.True(cfg.ProtectSpecialItems);
        Assert.False(cfg.AllowDiscardEquip);
        // 其余既有字段保持解析值
        Assert.Equal(1, cfg.Version);
        Assert.True(cfg.Enabled);
        Assert.False(cfg.DiscardHq);
    }

    /// <summary>
    /// 增量回归：含新字段的 JSON 往返后值正确（显式关闭保护 / 显式允许丢装备）。
    /// </summary>
    [Fact]
    public void Configuration_NewFieldsRoundTrip_PreservesValues()
    {
        var cfg = new Configuration
        {
            ProtectSpecialItems = false,
            AllowDiscardEquip = true,
        };

        var json = JsonConvert.SerializeObject(cfg);
        // 序列化结果应包含新字段
        Assert.Contains("ProtectSpecialItems", json);
        Assert.Contains("AllowDiscardEquip", json);

        var back = JsonConvert.DeserializeObject<Configuration>(json)!;
        Assert.False(back.ProtectSpecialItems);
        Assert.True(back.AllowDiscardEquip);
    }

    /// <summary>
    /// 增量回归：新配置实例的 HasShownWarning 默认值为 false，
    /// 保证“首次使用警告”只在第一次打开主窗口时弹出一次。
    /// </summary>
    [Fact]
    public void Configuration_DefaultHasShownWarning_IsFalse()
    {
        Assert.False(new Configuration().HasShownWarning);
    }
}
