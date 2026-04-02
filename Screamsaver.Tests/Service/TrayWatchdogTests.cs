using Screamsaver.Service;

namespace Screamsaver.Tests.Service;

/// <summary>
/// Tests for <see cref="TrayWatchdog.IsCorrectInstance"/> path-matching logic (TEST-3).
/// No real Process objects are needed — the method is extracted as an internal static.
/// </summary>
public class TrayWatchdogTests
{
    private const string ExpectedPath = @"C:\Program Files\Screamsaver\Screamsaver.TrayApp.exe";

    [Fact]
    public void IsCorrectInstance_SamePath_ReturnsTrue()
    {
        Assert.True(TrayWatchdog.IsCorrectInstance(ExpectedPath, ExpectedPath));
    }

    [Fact]
    public void IsCorrectInstance_DifferentCase_ReturnsTrue()
    {
        // Windows file system is case-insensitive
        Assert.True(TrayWatchdog.IsCorrectInstance(
            @"c:\program files\screamsaver\screamsaver.trayapp.exe",
            ExpectedPath));
    }

    [Fact]
    public void IsCorrectInstance_DifferentPath_ReturnsFalse()
    {
        Assert.False(TrayWatchdog.IsCorrectInstance(
            @"C:\Temp\impostor.exe",
            ExpectedPath));
    }

    [Fact]
    public void IsCorrectInstance_NullProcessPath_ReturnsFalse()
    {
        Assert.False(TrayWatchdog.IsCorrectInstance(null, ExpectedPath));
    }

    [Fact]
    public void IsCorrectInstance_EmptyProcessPath_ReturnsFalse()
    {
        Assert.False(TrayWatchdog.IsCorrectInstance(string.Empty, ExpectedPath));
    }

    [Fact]
    public void IsCorrectInstance_SubstringOfExpectedPath_ReturnsFalse()
    {
        // A process in a subdirectory of the expected directory should not match
        Assert.False(TrayWatchdog.IsCorrectInstance(
            @"C:\Program Files\Screamsaver\sub\Screamsaver.TrayApp.exe",
            ExpectedPath));
    }
}
