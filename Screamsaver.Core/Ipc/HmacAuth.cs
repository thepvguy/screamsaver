using System.Security.Cryptography;
using System.Text;

namespace Screamsaver.Core.Ipc;

/// <summary>
/// Shared PBKDF2 key-derivation and HMAC message-building logic for the control pipe
/// challenge-response protocol. Both <see cref="DefaultPipeClient"/> (client side) and
/// <c>PipeServer</c> (server side) use the same derivation so they always agree on the key.
/// Internal: external callers go through <see cref="IPipeClient"/> / IPipeServer.
/// </summary>
internal static class HmacAuth
{
    private const int Iterations = 100_000;
    private const int KeyLength  = 32;

    /// <summary>
    /// Fixed PBKDF2 salt used when no PIN credentials have been configured yet
    /// (recovery-password-only mode). Not a secret — its sole purpose is to prevent
    /// precomputation of the recovery key; the recovery password is already in the assembly.
    /// </summary>
    internal static readonly byte[] RecoverySalt =
    [
        0x53, 0x63, 0x72, 0x65, 0x61, 0x6D, 0x73, 0x61,
        0x76, 0x65, 0x72, 0x52, 0x65, 0x63, 0x6F, 0x76  // "ScreamsaverRecov"
    ];

    /// <summary>
    /// Derives a 32-byte HMAC key from <paramref name="pin"/> using PBKDF2-SHA256.
    /// The same call on client and server (with the same salt) produces the same key.
    /// </summary>
    internal static byte[] DeriveKey(string pin, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(pin), salt, Iterations, HashAlgorithmName.SHA256, KeyLength);

    /// <summary>
    /// Canonical input layout for the HMAC: <c>nonce (16 B) ‖ UTF-8(command)</c>.
    /// RULE-12: both the sender (<see cref="BuildMessage"/>) and the receiver
    /// (<c>PipeServer.VerifyHmac</c>) call this method — never duplicate the layout.
    /// </summary>
    internal static byte[] BuildInputBytes(byte[] nonce, string command)
    {
        var commandBytes = Encoding.UTF8.GetBytes(command);
        var data = new byte[nonce.Length + commandBytes.Length];
        nonce.CopyTo(data, 0);
        commandBytes.CopyTo(data, nonce.Length);
        return data;
    }

    /// <summary>
    /// Builds the wire message: <c>HMAC:{hmac_hex}|{command}</c>.
    /// The HMAC covers <c>nonce || UTF-8(command)</c> so the command cannot be
    /// replaced by an interceptor while keeping a valid HMAC (SEC-B).
    /// </summary>
    internal static string BuildMessage(byte[] hmacKey, byte[] nonce, string command)
    {
        var data = BuildInputBytes(nonce, command);
        var hmac = HMACSHA256.HashData(hmacKey, data);
        return $"HMAC:{Convert.ToHexString(hmac)}|{command}";
    }
}
