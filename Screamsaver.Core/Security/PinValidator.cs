using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Screamsaver.Core.Ipc;
using Screamsaver.Core.Models;
using BC = BCrypt.Net.BCrypt;

namespace Screamsaver.Core.Security;

public static class PinValidator
{
    /// <summary>Minimum number of characters a PIN must contain.</summary>
    public const int MinimumPinLength = 4;

    /// <summary>Hashes <paramref name="pin"/> with BCrypt (work factor 12) for UI verification.</summary>
    public static string HashPin(string pin) => BC.HashPassword(pin, workFactor: 12);

    /// <summary>
    /// Generates a random 16-byte PBKDF2 salt and derives the 32-byte HMAC key for the
    /// control pipe challenge-response. Returns both as uppercase hex strings suitable for
    /// storing in <see cref="PinCredentials.PinHmacKey"/> and <see cref="PinCredentials.PinHmacSalt"/>.
    /// </summary>
    public static (string HmacKey, string HmacSalt) DeriveHmacCredentials(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var key  = HmacAuth.DeriveKey(pin, salt);
        return (Convert.ToHexString(key), Convert.ToHexString(salt));
    }

    /// <summary>
    /// Returns true if <paramref name="candidate"/> matches either the stored BCrypt hash
    /// or the compiled-in recovery password.
    /// <paramref name="logger"/> is required so that unexpected BCrypt failures (corrupted
    /// hash, library fault, incompatible hash version) are always visible rather than
    /// silently returning "incorrect PIN" (RULE-8, RULE-9).
    /// Callers without a DI-injected logger must pass <c>NullLogger.Instance</c> explicitly.
    /// </summary>
    public static bool Verify(string candidate, string storedHash, ILogger logger)
    {
        if (RecoveryPassword.Verify(candidate))
            return true;

        if (string.IsNullOrEmpty(storedHash))
            return false;

        try { return BC.Verify(candidate, storedHash); }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[PinValidator] BCrypt.Verify threw unexpectedly — stored hash may be corrupted.");
            return false;
        }
    }
}
