namespace Screamsaver.Core.Security;

/// <summary>
/// Provides the compiled-in recovery password as a last-resort fallback
/// when the parent has forgotten their PIN.
///
/// The password is stored XOR-obfuscated across two byte arrays to prevent
/// trivial string extraction from the assembly. Change <see cref="_encoded"/>
/// and <see cref="_key"/> before shipping — generate them with the helper
/// method below and delete the helper from the release build.
///
/// KEEP THE PLAINTEXT PASSWORD IN A SECURE NOTE — it is shown once during
/// installation and cannot be recovered from the binary without effort.
/// </summary>
public static class RecoveryPassword
{
    // Default recovery password: "ScreamsaverAdmin1!"
    // These arrays are: plaintext XOR key, byte by byte.
    // To change the password, call GenerateArrays(newPassword) and replace these values.
    private static readonly byte[] _encoded =
    {
        0x02, 0x57, 0x56, 0x44, 0x1F, 0x1C, 0x5E, 0x18,
        0x49, 0x35, 0x0C, 0x34, 0x3B, 0x62, 0x21, 0x44,
        0x3D, 0x61
    };

    private static readonly byte[] _key =
    {
        0x51, 0x36, 0x27, 0x21, 0x76, 0x6D, 0x3E, 0x6B,
        0x22, 0x56, 0x6F, 0x57, 0x58, 0x13, 0x52, 0x35,
        0x5E, 0x12
    };

    public static string Get()
    {
        var result = new byte[_encoded.Length];
        for (int i = 0; i < _encoded.Length; i++)
            result[i] = (byte)(_encoded[i] ^ _key[i]);
        return System.Text.Encoding.UTF8.GetString(result);
    }

    public static bool Verify(string candidate)
    {
        // Use constant-time comparison to prevent timing attacks.
        var candidateBytes = System.Text.Encoding.UTF8.GetBytes(candidate);
        var expectedBytes  = System.Text.Encoding.UTF8.GetBytes(Get());
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(candidateBytes, expectedBytes);
    }

#if DEBUG
    /// <summary>
    /// Helper to regenerate the XOR arrays for a new recovery password.
    /// Call this in a scratch program, then paste the output above.
    /// Remove or gate behind #if DEBUG before shipping.
    /// </summary>
    public static (byte[] encoded, byte[] key) GenerateArrays(string plaintext)
    {
        var plain = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var key   = new byte[plain.Length];
        // SMELL-I: use the static API (no IDisposable allocation) instead of Create().
        System.Security.Cryptography.RandomNumberGenerator.Fill(key);
        var encoded = new byte[plain.Length];
        for (int i = 0; i < plain.Length; i++)
            encoded[i] = (byte)(plain[i] ^ key[i]);
        return (encoded, key);
    }
#endif
}
