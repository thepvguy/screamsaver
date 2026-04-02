namespace Screamsaver.Core;

/// <summary>
/// Thin abstraction over a registry key (or in-memory substitute for tests).
/// Hides the <c>Microsoft.Win32</c> dependency from call sites and allows
/// <see cref="SettingsRepository"/> to be tested without touching HKLM.
/// </summary>
internal interface IRegistryStore
{
    string GetString(string name, string defaultValue);
    int    GetInt   (string name, int    defaultValue);
    double GetDouble(string name, double defaultValue);
    void   SetString(string name, string value);
    void   SetInt   (string name, int    value);
    void   SetDouble(string name, double value);
}
