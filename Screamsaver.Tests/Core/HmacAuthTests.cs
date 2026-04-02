using System.Security.Cryptography;
using System.Text;
using Screamsaver.Core.Ipc;
using Screamsaver.Core.Security;

namespace Screamsaver.Tests.Core;

/// <summary>
/// Direct tests for <see cref="HmacAuth"/> (TEST-7).
/// Verifying these primitives directly means a regression in key derivation or message
/// building fails here rather than manifesting as a mysterious NACK in higher-level tests.
/// </summary>
public class HmacAuthTests
{
    // ── DeriveKey ─────────────────────────────────────────────────────────────

    [Fact]
    public void DeriveKey_IsDeterministicForEqualInputs()
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var key1 = HmacAuth.DeriveKey("testpin", salt);
        var key2 = HmacAuth.DeriveKey("testpin", salt);
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void DeriveKey_ProducesDifferentKeyForDifferentPin()
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var key1 = HmacAuth.DeriveKey("pin1", salt);
        var key2 = HmacAuth.DeriveKey("pin2", salt);
        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void DeriveKey_ProducesDifferentKeyForDifferentSalt()
    {
        var salt1 = RandomNumberGenerator.GetBytes(16);
        var salt2 = RandomNumberGenerator.GetBytes(16);
        var key1  = HmacAuth.DeriveKey("samepin", salt1);
        var key2  = HmacAuth.DeriveKey("samepin", salt2);
        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void DeriveKey_Produces32ByteOutput()
    {
        var key = HmacAuth.DeriveKey("pin", RandomNumberGenerator.GetBytes(16));
        Assert.Equal(32, key.Length);
    }

    // ── BuildInputBytes ───────────────────────────────────────────────────────

    [Fact]
    public void BuildInputBytes_LayoutIs_Nonce_Then_UTF8Command()
    {
        var nonce = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var data  = HmacAuth.BuildInputBytes(nonce, "PAUSE");

        // First 4 bytes must be the nonce
        Assert.Equal(nonce, data[..4]);
        // Remaining bytes must be UTF-8 "PAUSE"
        Assert.Equal(Encoding.UTF8.GetBytes("PAUSE"), data[4..]);
    }

    [Fact]
    public void BuildInputBytes_DifferentNonces_ProduceDifferentData()
    {
        var n1 = RandomNumberGenerator.GetBytes(16);
        var n2 = RandomNumberGenerator.GetBytes(16);
        Assert.NotEqual(HmacAuth.BuildInputBytes(n1, "PAUSE"), HmacAuth.BuildInputBytes(n2, "PAUSE"));
    }

    [Fact]
    public void BuildInputBytes_DifferentCommands_ProduceDifferentData()
    {
        var nonce = RandomNumberGenerator.GetBytes(16);
        Assert.NotEqual(HmacAuth.BuildInputBytes(nonce, "PAUSE"), HmacAuth.BuildInputBytes(nonce, "RESUME"));
    }

    // ── BuildMessage ──────────────────────────────────────────────────────────

    [Fact]
    public void BuildMessage_HasExpectedFormat()
    {
        var key   = HmacAuth.DeriveKey("pin", RandomNumberGenerator.GetBytes(16));
        var nonce = RandomNumberGenerator.GetBytes(16);
        var msg   = HmacAuth.BuildMessage(key, nonce, "PAUSE");

        Assert.StartsWith("HMAC:", msg, StringComparison.Ordinal);
        Assert.Contains("|PAUSE", msg, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMessage_HmacHexIs64Chars()
    {
        var key   = HmacAuth.DeriveKey("pin", RandomNumberGenerator.GetBytes(16));
        var nonce = RandomNumberGenerator.GetBytes(16);
        var msg   = HmacAuth.BuildMessage(key, nonce, "PAUSE");

        // Format: "HMAC:{64 hex chars}|PAUSE"
        var hmacHex = msg["HMAC:".Length..msg.IndexOf('|')];
        Assert.Equal(64, hmacHex.Length);
        Assert.Matches("^[0-9A-F]+$", hmacHex);
    }

    [Fact]
    public void BuildMessage_ChangingNonce_ChangesMac()
    {
        var key = HmacAuth.DeriveKey("pin", RandomNumberGenerator.GetBytes(16));
        var n1  = RandomNumberGenerator.GetBytes(16);
        var n2  = RandomNumberGenerator.GetBytes(16);
        Assert.NotEqual(HmacAuth.BuildMessage(key, n1, "PAUSE"), HmacAuth.BuildMessage(key, n2, "PAUSE"));
    }

    [Fact]
    public void BuildMessage_ChangingCommand_ChangesMac()
    {
        var key   = HmacAuth.DeriveKey("pin", RandomNumberGenerator.GetBytes(16));
        var nonce = RandomNumberGenerator.GetBytes(16);
        Assert.NotEqual(HmacAuth.BuildMessage(key, nonce, "PAUSE"), HmacAuth.BuildMessage(key, nonce, "RESUME"));
    }

    // ── RecoverySalt ──────────────────────────────────────────────────────────

    [Fact]
    public void RecoverySalt_IsUtf8EncodingOf_ScreamsaverRecov()
    {
        Assert.Equal(Encoding.UTF8.GetBytes("ScreamsaverRecov"), HmacAuth.RecoverySalt);
    }

    // ── Round-trip: DeriveKey + BuildMessage + verify ─────────────────────────

    [Fact]
    public void RoundTrip_BuildInputBytesMatchesBuildMessage()
    {
        // Verify that BuildMessage uses BuildInputBytes internally by confirming
        // the HMAC in the message can be reproduced via BuildInputBytes + HMACSHA256.
        var salt  = RandomNumberGenerator.GetBytes(16);
        var key   = HmacAuth.DeriveKey("roundtrippin", salt);
        var nonce = RandomNumberGenerator.GetBytes(16);
        const string command = "PAUSE";

        var msg     = HmacAuth.BuildMessage(key, nonce, command);
        var hmacHex = msg["HMAC:".Length..msg.IndexOf('|')];

        var expected = HMACSHA256.HashData(key, HmacAuth.BuildInputBytes(nonce, command));
        Assert.Equal(Convert.ToHexString(expected), hmacHex);
    }
}
