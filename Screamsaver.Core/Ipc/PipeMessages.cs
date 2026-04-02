namespace Screamsaver.Core.Ipc;

public static class PipeMessages
{
    public const string Blackout = "BLACKOUT";
    public const string Pause = "PAUSE";
    public const string Resume = "RESUME";
    public const string UpdateSettingsPrefix = "UPDATE_SETTINGS:";

    public static string UpdateSettings(string json) => UpdateSettingsPrefix + json;

    public static bool IsUpdateSettings(string message) =>
        message.StartsWith(UpdateSettingsPrefix, StringComparison.Ordinal);

    public static string ExtractSettingsJson(string message) =>
        message[UpdateSettingsPrefix.Length..];
}
