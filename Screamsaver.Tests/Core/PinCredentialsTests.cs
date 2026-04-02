using Screamsaver.Core.Models;

namespace Screamsaver.Tests.Core;

public class PinCredentialsTests
{
    [Fact]
    public void Empty_IsConfigured_ReturnsFalse()
    {
        Assert.False(PinCredentials.Empty.IsConfigured);
    }

    [Fact]
    public void WithPinHashAndHmacKey_IsConfigured_ReturnsTrue()
    {
        var creds = new PinCredentials("some-bcrypt-hash", "some-hmac-key", "some-hmac-salt");
        Assert.True(creds.IsConfigured);
    }

    [Fact]
    public void WithPinHashOnly_NoHmacKey_IsConfigured_ReturnsFalse()
    {
        // IsConfigured requires both PinHash AND PinHmacKey (SEC-5)
        var creds = new PinCredentials("some-bcrypt-hash", string.Empty, string.Empty);
        Assert.False(creds.IsConfigured);
    }

    [Fact]
    public void Empty_HasEmptyStrings()
    {
        Assert.Equal(string.Empty, PinCredentials.Empty.PinHash);
        Assert.Equal(string.Empty, PinCredentials.Empty.PinHmacKey);
        Assert.Equal(string.Empty, PinCredentials.Empty.PinHmacSalt);
    }

    [Fact]
    public void SameValues_AreEqual()
    {
        var a = new PinCredentials("hash", "hmacKey", "hmacSalt");
        var b = new PinCredentials("hash", "hmacKey", "hmacSalt");
        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentValues_AreNotEqual()
    {
        var a = new PinCredentials("hash1", "key1", "salt1");
        var b = new PinCredentials("hash2", "key2", "salt2");
        Assert.NotEqual(a, b);
    }
}
