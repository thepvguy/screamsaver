namespace Screamsaver.Core.Models;

/// <summary>
/// Immutable PIN credentials stored in the registry separately from tunable
/// <see cref="AppSettings"/>:
/// <list type="bullet">
/// <item><term>PinHash</term><description>BCrypt hash for local UI PIN verification.</description></item>
/// <item><term>PinHmacKey</term><description>
///   PBKDF2-SHA256(pin, PinHmacSalt, 100 000 iterations, 32 bytes) stored as uppercase hex.
///   Used by the service to verify the pipe challenge-response HMAC without ever seeing the PIN.
/// </description></item>
/// <item><term>PinHmacSalt</term><description>
///   Random 16-byte PBKDF2 salt (uppercase hex). Sent in the pipe challenge so the client can
///   derive the same key. Salts prevent precomputed attacks; they are not secret.
/// </description></item>
/// </list>
/// </summary>
public sealed record PinCredentials(string PinHash, string PinHmacKey, string PinHmacSalt)
{
    /// <summary>No credentials have been configured yet.</summary>
    public static readonly PinCredentials Empty = new(string.Empty, string.Empty, string.Empty);

    /// <summary>
    /// True when a PIN has been set and the HMAC key is present.
    /// False means the service falls back to recovery-password-only pipe authentication.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrEmpty(PinHash) && !string.IsNullOrEmpty(PinHmacKey);
}
