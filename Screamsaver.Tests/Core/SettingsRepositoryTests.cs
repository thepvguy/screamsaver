using Screamsaver.Core;
using Screamsaver.Core.Models;

namespace Screamsaver.Tests.Core;

/// <summary>
/// In-memory IRegistryStore for tests — no HKLM access required.
/// Lives here rather than in the test project root so it stays close to its only consumers.
/// </summary>
internal sealed class MemoryRegistryStore : IRegistryStore
{
    private readonly Dictionary<string, object> _data = new(StringComparer.OrdinalIgnoreCase);

    public string GetString(string name, string defaultValue) =>
        _data.TryGetValue(name, out var v) ? (string)v : defaultValue;

    public int GetInt(string name, int defaultValue) =>
        _data.TryGetValue(name, out var v) ? (int)v : defaultValue;

    public double GetDouble(string name, double defaultValue) =>
        _data.TryGetValue(name, out var v) ? (double)v : defaultValue;

    public void SetString(string name, string value) => _data[name] = value;
    public void SetInt   (string name, int    value) => _data[name] = value;
    public void SetDouble(string name, double value) => _data[name] = value;
}

/// <summary>IRegistryStore that throws on every call — used to verify error-path behaviour.</summary>
internal sealed class ThrowingRegistryStore : IRegistryStore
{
    public string GetString(string name, string _) => throw new InvalidOperationException("store failure");
    public int    GetInt   (string name, int    _) => throw new InvalidOperationException("store failure");
    public double GetDouble(string name, double _) => throw new InvalidOperationException("store failure");
    public void SetString(string name, string value) => throw new InvalidOperationException("store failure");
    public void SetInt   (string name, int    value) => throw new InvalidOperationException("store failure");
    public void SetDouble(string name, double value) => throw new InvalidOperationException("store failure");
}

public class SettingsRepositoryTests
{
    // ── Load defaults ─────────────────────────────────────────────────────────

    [Fact]
    public void Load_ReturnsDefaults_WhenStoreIsEmpty()
    {
        var repo = new SettingsRepository(new MemoryRegistryStore());
        var s = repo.Load();

        Assert.Equal(new AppSettings(), s);
    }

    [Fact]
    public void LoadCredentials_ReturnsEmpty_WhenStoreIsEmpty()
    {
        var repo = new SettingsRepository(new MemoryRegistryStore());
        Assert.Equal(PinCredentials.Empty, repo.LoadCredentials());
    }

    // ── Round-trips ───────────────────────────────────────────────────────────

    [Fact]
    public void Save_ThenLoad_RoundTrips_AllFields()
    {
        var store = new MemoryRegistryStore();
        var repo = new SettingsRepository(store);
        var original = new AppSettings
        {
            ThresholdDb      = -35.5,
            CooldownSeconds  = 60,
            FadeInDurationMs = 500,
            HoldDurationMs   = 2000,
            FadeOutDurationMs= 3000,
            MaxOpacity       = 0.85,
            OverlayColor     = "#FF0000",
            OverlayImagePath = @"C:\img.png",
        };

        repo.Save(original);
        var loaded = repo.Load();

        Assert.Equal(original, loaded);
    }

    [Fact]
    public void SaveCredentials_ThenLoadCredentials_RoundTrips()
    {
        var store = new MemoryRegistryStore();
        var repo = new SettingsRepository(store);
        var creds = new PinCredentials("bcrypt-hash-value", "hmac-key-hex", "hmac-salt-hex");

        repo.SaveCredentials(creds);
        var loaded = repo.LoadCredentials();

        Assert.Equal(creds, loaded);
    }

    [Fact]
    public void Settings_And_Credentials_UseSameStore_WithoutInterference()
    {
        // Saving credentials must not corrupt settings and vice versa.
        var store = new MemoryRegistryStore();
        var repo = new SettingsRepository(store);
        var settings = new AppSettings { ThresholdDb = -40.0 };
        var creds = new PinCredentials("hash", "hmacKey", "hmacSalt");

        repo.Save(settings);
        repo.SaveCredentials(creds);

        Assert.Equal(settings, repo.Load());
        Assert.Equal(creds,    repo.LoadCredentials());
    }

    // ── Error paths ───────────────────────────────────────────────────────────

    [Fact]
    public void Load_ReturnsDefaults_OnStoreReadFailure()
    {
        var repo = new SettingsRepository(new ThrowingRegistryStore());
        var s = repo.Load();
        Assert.Equal(new AppSettings(), s);
    }

    [Fact]
    public void LoadCredentials_ReturnsEmpty_OnStoreReadFailure()
    {
        var repo = new SettingsRepository(new ThrowingRegistryStore());
        Assert.Equal(PinCredentials.Empty, repo.LoadCredentials());
    }

    [Fact]
    public void Save_ThrowsInvalidOperationException_OnStoreWriteFailure()
    {
        var repo = new SettingsRepository(new ThrowingRegistryStore());
        Assert.Throws<InvalidOperationException>(() => repo.Save(new AppSettings()));
    }

    [Fact]
    public void SaveCredentials_ThrowsInvalidOperationException_OnStoreWriteFailure()
    {
        var repo = new SettingsRepository(new ThrowingRegistryStore());
        Assert.Throws<InvalidOperationException>(() => repo.SaveCredentials(new PinCredentials("a", "b", "c")));
    }
}
