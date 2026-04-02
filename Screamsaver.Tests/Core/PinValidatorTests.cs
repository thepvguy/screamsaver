using Microsoft.Extensions.Logging.Abstractions;
using Screamsaver.Core.Security;

namespace Screamsaver.Tests.Core;

public class PinValidatorTests
{
    // ── MinimumPinLength (SEC-A) ──────────────────────────────────────────────

    [Fact]
    public void MinimumPinLength_IsAtLeastFour()
    {
        Assert.True(PinValidator.MinimumPinLength >= 4);
    }


    [Fact]
    public void HashPin_ProducesBCryptHash()
    {
        var hash = PinValidator.HashPin("1234");
        Assert.StartsWith("$2", hash, StringComparison.Ordinal); // BCrypt prefix
    }

    [Fact]
    public void HashPin_IsDifferentEachCall_DueToDifferentSalt()
    {
        var h1 = PinValidator.HashPin("1234");
        var h2 = PinValidator.HashPin("1234");
        Assert.NotEqual(h1, h2); // different salts → different hashes
    }

    [Fact]
    public void Verify_ReturnsTrueForMatchingPinAndHash()
    {
        var hash = PinValidator.HashPin("mysecret");
        Assert.True(PinValidator.Verify("mysecret", hash, NullLogger.Instance));
    }

    [Fact]
    public void Verify_ReturnsFalseForWrongPin()
    {
        var hash = PinValidator.HashPin("mysecret");
        Assert.False(PinValidator.Verify("wrongpin", hash, NullLogger.Instance));
    }

    [Fact]
    public void Verify_ReturnsFalseForEmptyHash()
    {
        Assert.False(PinValidator.Verify("anypin", string.Empty, NullLogger.Instance));
    }

    [Fact]
    public void Verify_AcceptsRecoveryPasswordRegardlessOfStoredHash()
    {
        // Recovery password bypasses BCrypt check entirely
        var recovery = RecoveryPassword.Get();
        Assert.True(PinValidator.Verify(recovery, "some-hash-that-does-not-match", NullLogger.Instance));
    }

    [Fact]
    public void DeriveHmacCredentials_ProducesCorrectLengthHexStrings()
    {
        var (key, salt) = PinValidator.DeriveHmacCredentials("testpin");
        Assert.Equal(64, key.Length);  // 32-byte PBKDF2 key = 64 hex chars
        Assert.Equal(32, salt.Length); // 16-byte random salt = 32 hex chars
        Assert.Matches("^[0-9A-F]+$", key);
        Assert.Matches("^[0-9A-F]+$", salt);
    }

    [Fact]
    public void DeriveHmacCredentials_EachCallHasRandomSalt()
    {
        var (_, salt1) = PinValidator.DeriveHmacCredentials("testpin");
        var (_, salt2) = PinValidator.DeriveHmacCredentials("testpin");
        Assert.NotEqual(salt1, salt2); // 16-byte random salt → different each call
    }
}
