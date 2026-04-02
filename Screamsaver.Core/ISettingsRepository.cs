using Screamsaver.Core.Models;

namespace Screamsaver.Core;

public interface ISettingsRepository
{
    AppSettings    Load();
    void           Save(AppSettings settings);
    PinCredentials LoadCredentials();
    void           SaveCredentials(PinCredentials credentials);
    /// <summary>
    /// Returns true if monitoring is currently paused (persisted across service restarts).
    /// RULE-13: pause state is operational, not a preference — loss on restart silently
    /// re-enables enforcement against a parent's explicit intent.
    /// </summary>
    bool           LoadPauseState();
    /// <summary>Persists the pause state immediately. See <see cref="LoadPauseState"/>.</summary>
    void           SavePauseState(bool paused);
}
