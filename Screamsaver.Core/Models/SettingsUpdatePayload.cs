namespace Screamsaver.Core.Models;

/// <summary>
/// Payload for the UPDATE_SETTINGS pipe command.
/// <see cref="Credentials"/> is non-null only when the PIN is being rotated
/// alongside a settings change; the service saves credentials to registry and
/// updates its cached credentials on the same transaction.
/// </summary>
public sealed record SettingsUpdatePayload(AppSettings Settings, PinCredentials? Credentials = null);
