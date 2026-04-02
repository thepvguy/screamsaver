using System.Globalization;
using Microsoft.Win32;

namespace Screamsaver.Core;

/// <summary>
/// Production <see cref="IRegistryStore"/> backed by <c>HKLM\SOFTWARE\Screamsaver</c>
/// (or whichever key path is passed to the constructor).
/// Each call opens and closes the key independently — registry handles are cheap
/// and this keeps the implementation simple.
/// </summary>
internal sealed class WindowsRegistryStore : IRegistryStore
{
    private readonly string _keyPath;

    internal WindowsRegistryStore(string keyPath) => _keyPath = keyPath;

    public string GetString(string name, string defaultValue)
    {
        using var key = Registry.LocalMachine.OpenSubKey(_keyPath, writable: false);
        return key?.GetValue(name) as string ?? defaultValue;
    }

    public int GetInt(string name, int defaultValue)
    {
        using var key = Registry.LocalMachine.OpenSubKey(_keyPath, writable: false);
        if (key is null) return defaultValue;
        return Convert.ToInt32(key.GetValue(name, defaultValue));
    }

    public double GetDouble(string name, double defaultValue)
    {
        using var key = Registry.LocalMachine.OpenSubKey(_keyPath, writable: false);
        if (key is null) return defaultValue;
        var raw = key.GetValue(name);
        if (raw is null) return defaultValue;
        return Convert.ToDouble(raw, CultureInfo.InvariantCulture);
    }

    public void SetString(string name, string value)
    {
        using var key = Registry.LocalMachine.CreateSubKey(_keyPath, writable: true);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void SetInt(string name, int value)
    {
        using var key = Registry.LocalMachine.CreateSubKey(_keyPath, writable: true);
        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    public void SetDouble(string name, double value)
    {
        using var key = Registry.LocalMachine.CreateSubKey(_keyPath, writable: true);
        key.SetValue(name, value.ToString(CultureInfo.InvariantCulture), RegistryValueKind.String);
    }
}
