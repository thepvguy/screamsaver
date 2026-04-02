using Screamsaver.Core.Security;

namespace Screamsaver.Tests.Core;

public class RecoveryPasswordTests
{
    [Fact]
    public void Get_ReturnsNonEmptyString()
    {
        var password = RecoveryPassword.Get();
        Assert.False(string.IsNullOrEmpty(password));
    }

    [Fact]
    public void Get_IsIdempotent()
    {
        // XOR decode is deterministic; calling twice must produce identical output
        Assert.Equal(RecoveryPassword.Get(), RecoveryPassword.Get());
    }

    [Fact]
    public void Verify_ReturnsTrueForCorrectPassword()
    {
        var password = RecoveryPassword.Get();
        Assert.True(RecoveryPassword.Verify(password));
    }

    [Fact]
    public void Verify_ReturnsFalseForWrongPassword()
    {
        Assert.False(RecoveryPassword.Verify("wrong-password"));
    }

    [Fact]
    public void Verify_ReturnsFalseForEmpty()
    {
        Assert.False(RecoveryPassword.Verify(string.Empty));
    }

    [Fact]
    public void Verify_IsCaseSensitive()
    {
        var password = RecoveryPassword.Get();
        Assert.False(RecoveryPassword.Verify(password.ToLowerInvariant()));
    }
}
