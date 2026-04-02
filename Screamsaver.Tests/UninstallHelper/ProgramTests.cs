using Microsoft.Extensions.Logging.Abstractions;
using Screamsaver.Core.Models;
using Screamsaver.Core.Security;
using Screamsaver.UninstallHelper;

namespace Screamsaver.Tests.UninstallHelper;

public class ProgramTests
{
    // ── ParseArgs ─────────────────────────────────────────────────────────────

    [Fact]
    public void ParseArgs_NoArgs_ReturnsFalseAndNullPin()
    {
        var result = UninstallLogic.ParseArgs([]);
        Assert.False(result.Silent);
        Assert.Null(result.Pin);
        Assert.False(result.PinFromStdin);
    }

    [Fact]
    public void ParseArgs_SilentOnly_ReturnsTrueAndNullPin()
    {
        var result = UninstallLogic.ParseArgs(["--silent"]);
        Assert.True(result.Silent);
        Assert.Null(result.Pin);
    }

    [Fact]
    public void ParseArgs_SilentWithPin_ReturnsBoth()
    {
        var result = UninstallLogic.ParseArgs(["--silent", "--pin", "1234"]);
        Assert.True(result.Silent);
        Assert.Equal("1234", result.Pin);
    }

    [Fact]
    public void ParseArgs_PinWithoutSilent_ReturnsSilentFalse()
    {
        var result = UninstallLogic.ParseArgs(["--pin", "1234"]);
        Assert.False(result.Silent);
        Assert.Equal("1234", result.Pin);
    }

    [Fact]
    public void ParseArgs_PinAtEndWithNoValue_IgnoresDanglingFlag()
    {
        // "--pin" with no following token — pin stays null
        var result = UninstallLogic.ParseArgs(["--silent", "--pin"]);
        Assert.True(result.Silent);
        Assert.Null(result.Pin);
    }

    [Fact]
    public void ParseArgs_UnknownArgs_AreIgnored()
    {
        var result = UninstallLogic.ParseArgs(["--unknown", "--silent", "--pin", "abc"]);
        Assert.True(result.Silent);
        Assert.Equal("abc", result.Pin);
    }

    [Fact]
    public void ParseArgs_PinStdin_SetsPinFromStdinTrue()
    {
        var result = UninstallLogic.ParseArgs(["--silent", "--pin-stdin"]);
        Assert.True(result.Silent);
        Assert.True(result.PinFromStdin);
        Assert.Null(result.Pin);
    }

    [Fact]
    public void ParseArgs_PinStdin_DoesNotConflictWithSilent()
    {
        // --pin-stdin is independent of --pin; both can coexist (Program.Main prefers stdin)
        var result = UninstallLogic.ParseArgs(["--silent", "--pin", "1234", "--pin-stdin"]);
        Assert.True(result.PinFromStdin);
        Assert.Equal("1234", result.Pin);
    }

    [Fact]
    public void ParseArgs_PinFile_SetsPinFilePath()
    {
        var result = UninstallLogic.ParseArgs(["--silent", "--pin-file", @"C:\tmp\ss.pin"]);
        Assert.True(result.Silent);
        Assert.Equal(@"C:\tmp\ss.pin", result.PinFile);
    }

    [Fact]
    public void ParseArgs_PinFileWithNoValue_IgnoresDanglingFlag()
    {
        var result = UninstallLogic.ParseArgs(["--silent", "--pin-file"]);
        Assert.Null(result.PinFile);
    }

    // ── ReadAndDeletePinFile ──────────────────────────────────────────────────

    [Fact]
    public void ReadAndDeletePinFile_ReturnsTrimmmedContentsAndDeletesFile()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "5678\r\n");

        var result = UninstallLogic.ReadAndDeletePinFile(path);

        Assert.Equal("5678", result);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ReadAndDeletePinFile_MissingFile_ReturnsNull()
    {
        var result = UninstallLogic.ReadAndDeletePinFile(@"C:\does\not\exist\pin.tmp");
        Assert.Null(result);
    }

    // ── RunSilent ─────────────────────────────────────────────────────────────

    [Fact]
    public void RunSilent_NullPin_ReturnsOne()
    {
        Assert.Equal(1, UninstallLogic.RunSilent(null, PinCredentials.Empty, NullLogger.Instance));
    }

    [Fact]
    public void RunSilent_EmptyPin_ReturnsOne()
    {
        Assert.Equal(1, UninstallLogic.RunSilent(string.Empty, PinCredentials.Empty, NullLogger.Instance));
    }

    [Fact]
    public void RunSilent_CorrectPin_ReturnsZero()
    {
        const string pin = "correct123";
        var creds = new PinCredentials(
            PinHash:     PinValidator.HashPin(pin),
            PinHmacKey:  string.Empty,
            PinHmacSalt: string.Empty);

        Assert.Equal(0, UninstallLogic.RunSilent(pin, creds, NullLogger.Instance));
    }

    [Fact]
    public void RunSilent_WrongPin_ReturnsOne()
    {
        var creds = new PinCredentials(
            PinHash:     PinValidator.HashPin("correct"),
            PinHmacKey:  string.Empty,
            PinHmacSalt: string.Empty);

        Assert.Equal(1, UninstallLogic.RunSilent("wrong", creds, NullLogger.Instance));
    }

    [Fact]
    public void RunSilent_RecoveryPassword_ReturnsZero()
    {
        // Recovery password works regardless of the stored PinHash
        var creds = new PinCredentials(
            PinHash:     PinValidator.HashPin("somepin"),
            PinHmacKey:  string.Empty,
            PinHmacSalt: string.Empty);
        var recovery = RecoveryPassword.Get();

        Assert.Equal(0, UninstallLogic.RunSilent(recovery, creds, NullLogger.Instance));
    }

    [Fact]
    public void RunSilent_EmptyCredentials_WrongPin_ReturnsOne()
    {
        // No PIN has been set yet — any non-recovery PIN should fail
        Assert.Equal(1, UninstallLogic.RunSilent("anypin", PinCredentials.Empty, NullLogger.Instance));
    }
}
