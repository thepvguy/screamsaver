using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Screamsaver.Core.Models;

namespace Screamsaver.Core;

/// <summary>
/// Reads and writes <see cref="AppSettings"/> and <see cref="PinCredentials"/> using an
/// <see cref="IRegistryStore"/>. The production store targets HKLM\SOFTWARE\Screamsaver;
/// tests supply a <c>MemoryRegistryStore</c> so no HKLM access is required.
/// </summary>
public class SettingsRepository : ISettingsRepository
{
    /// <summary>
    /// Singleton for call sites without a DI container (UninstallHelper).
    /// All other code should receive <see cref="ISettingsRepository"/> via constructor injection.
    /// </summary>
    public static readonly SettingsRepository Instance = new(new WindowsRegistryStore(Constants.RegistryKeyPath));

    private readonly IRegistryStore _store;
    private readonly ILogger<SettingsRepository> _logger;

    internal SettingsRepository(IRegistryStore store, ILogger<SettingsRepository>? logger = null)
    {
        _store  = store;
        _logger = logger ?? NullLogger<SettingsRepository>.Instance;
    }

    public AppSettings Load()
    {
        try
        {
            return new AppSettings
            {
                ThresholdDb       = _store.GetDouble(nameof(AppSettings.ThresholdDb),       -20.0),
                CooldownSeconds   = _store.GetInt   (nameof(AppSettings.CooldownSeconds),   30),
                FadeInDurationMs  = _store.GetInt   (nameof(AppSettings.FadeInDurationMs),  0),
                HoldDurationMs    = _store.GetInt   (nameof(AppSettings.HoldDurationMs),    0),
                FadeOutDurationMs = _store.GetInt   (nameof(AppSettings.FadeOutDurationMs), 5000),
                MaxOpacity        = _store.GetDouble(nameof(AppSettings.MaxOpacity),        1.0),
                OverlayColor      = _store.GetString(nameof(AppSettings.OverlayColor),      "#000000"),
                OverlayImagePath  = _store.GetString(nameof(AppSettings.OverlayImagePath),  string.Empty),
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SettingsRepository] Load failed.");
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            _store.SetDouble(nameof(AppSettings.ThresholdDb),       settings.ThresholdDb);
            _store.SetInt   (nameof(AppSettings.CooldownSeconds),   settings.CooldownSeconds);
            _store.SetInt   (nameof(AppSettings.FadeInDurationMs),  settings.FadeInDurationMs);
            _store.SetInt   (nameof(AppSettings.HoldDurationMs),    settings.HoldDurationMs);
            _store.SetInt   (nameof(AppSettings.FadeOutDurationMs), settings.FadeOutDurationMs);
            _store.SetDouble(nameof(AppSettings.MaxOpacity),        settings.MaxOpacity);
            _store.SetString(nameof(AppSettings.OverlayColor),      settings.OverlayColor);
            _store.SetString(nameof(AppSettings.OverlayImagePath),  settings.OverlayImagePath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to save settings to registry.", ex);
        }
    }

    public PinCredentials LoadCredentials()
    {
        try
        {
            return new PinCredentials(
                PinHash:     _store.GetString(nameof(PinCredentials.PinHash),     string.Empty),
                PinHmacKey:  _store.GetString(nameof(PinCredentials.PinHmacKey),  string.Empty),
                PinHmacSalt: _store.GetString(nameof(PinCredentials.PinHmacSalt), string.Empty)
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SettingsRepository] LoadCredentials failed.");
            return PinCredentials.Empty;
        }
    }

    public void SaveCredentials(PinCredentials credentials)
    {
        // RULE-14: write in dependency order — the service reads Salt+Key first (for HMAC
        // auth); PinHash is only used by the tray UI for local PIN prompts.
        //
        // Partial-failure modes (each step may throw; recovery password always works):
        //   After Salt only:    new Salt written; old Key won't match it → auth fails for
        //                       user PIN (recovery still works); restart will re-derive.
        //   After Salt + Key:   pipe auth works with new PIN; tray still prompts old PIN
        //                       (uses old PinHash) — inconsistent but not a lockout.
        //   After all three:    fully consistent; both service and tray use new PIN.
        try
        {
            _store.SetString(nameof(PinCredentials.PinHmacSalt), credentials.PinHmacSalt);
            _store.SetString(nameof(PinCredentials.PinHmacKey),  credentials.PinHmacKey);
            _store.SetString(nameof(PinCredentials.PinHash),     credentials.PinHash);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to save credentials to registry.", ex);
        }
    }

    public bool LoadPauseState()
    {
        try   { return _store.GetInt("PauseState", 0) != 0; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SettingsRepository] LoadPauseState failed; defaulting to not paused.");
            return false;
        }
    }

    public void SavePauseState(bool paused)
    {
        try   { _store.SetInt("PauseState", paused ? 1 : 0); }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to save pause state to registry.", ex);
        }
    }
}
