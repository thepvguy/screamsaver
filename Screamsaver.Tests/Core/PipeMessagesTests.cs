using Screamsaver.Core.Ipc;

namespace Screamsaver.Tests.Core;

public class PipeMessagesTests
{
    [Fact]
    public void UpdateSettings_PrefixesJsonCorrectly()
    {
        const string json = """{"ThresholdDb":-20.0}""";
        var message = PipeMessages.UpdateSettings(json);
        Assert.StartsWith(PipeMessages.UpdateSettingsPrefix, message, StringComparison.Ordinal);
        Assert.EndsWith(json, message, StringComparison.Ordinal);
    }

    [Fact]
    public void IsUpdateSettings_ReturnsTrueForPrefixedMessage()
    {
        var message = PipeMessages.UpdateSettings("{}");
        Assert.True(PipeMessages.IsUpdateSettings(message));
    }

    [Fact]
    public void IsUpdateSettings_ReturnsFalseForOtherCommands()
    {
        Assert.False(PipeMessages.IsUpdateSettings(PipeMessages.Pause));
        Assert.False(PipeMessages.IsUpdateSettings(PipeMessages.Resume));
        Assert.False(PipeMessages.IsUpdateSettings(PipeMessages.Blackout));
    }

    [Fact]
    public void ExtractSettingsJson_ReturnsJsonPortion()
    {
        const string json = """{"ThresholdDb":-30.0,"CooldownSeconds":60}""";
        var message = PipeMessages.UpdateSettings(json);
        Assert.Equal(json, PipeMessages.ExtractSettingsJson(message));
    }

    [Fact]
    public void RoundTrip_UpdateSettingsJsonSurvivesExtraction()
    {
        const string original = """{"OverlayColor":"#FF0000"}""";
        var extracted = PipeMessages.ExtractSettingsJson(PipeMessages.UpdateSettings(original));
        Assert.Equal(original, extracted);
    }
}
