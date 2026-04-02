using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Screamsaver.Core;

namespace Screamsaver.Service;

/// <summary>
/// Monitors whether the tray app is running in the active user session.
/// Relaunches it via CreateProcessAsUser if it is missing or has been replaced by an
/// impostor process (same name, different executable path).
/// </summary>
public class TrayWatchdog : ITrayWatchdog
{
    private readonly ILogger<TrayWatchdog> _logger;
    private readonly string _trayAppPath;

    public TrayWatchdog(ILogger<TrayWatchdog> logger)
    {
        _logger     = logger;
        _trayAppPath = Path.Combine(AppContext.BaseDirectory, Core.Constants.TrayAppExeName);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { EnsureTrayRunning(); }
            catch (Exception ex) { _logger.LogError(ex, "TrayWatchdog error."); }

            await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private void EnsureTrayRunning()
    {
        var trayName  = Path.GetFileNameWithoutExtension(Core.Constants.TrayAppExeName);
        var processes = Process.GetProcessesByName(trayName);

        var correctInstanceRunning = processes.Any(p =>
        {
            try { return IsCorrectInstance(p.MainModule?.FileName, _trayAppPath); }
            catch { return false; }
        });

        if (correctInstanceRunning) return;

        if (!File.Exists(_trayAppPath))
        {
            _logger.LogWarning("Tray app not found at {Path}", _trayAppPath);
            return;
        }

        _logger.LogInformation("Tray app not running — launching {Path}", _trayAppPath);
        LaunchInUserSession(_trayAppPath);
    }

    /// <summary>
    /// Returns true when <paramref name="processPath"/> refers to the expected executable.
    /// Case-insensitive for Windows file system compatibility.
    /// Internal so unit tests can verify the path-matching logic without real Process objects.
    /// </summary>
    internal static bool IsCorrectInstance(string? processPath, string expectedPath) =>
        string.Equals(processPath, expectedPath, StringComparison.OrdinalIgnoreCase);

    private void LaunchInUserSession(string exePath)
    {
        uint sessionId = NativeMethods.WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF)
            throw new InvalidOperationException("No active console session.");

        if (!NativeMethods.WTSQueryUserToken(sessionId, out var userToken))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WTSQueryUserToken failed.");

        try
        {
            var si  = new NativeMethods.STARTUPINFO { cb = Marshal.SizeOf<NativeMethods.STARTUPINFO>() };
            var env = IntPtr.Zero;

            NativeMethods.CreateEnvironmentBlock(out env, userToken, false);
            try
            {
                var flags = NativeMethods.CREATE_UNICODE_ENVIRONMENT | NativeMethods.NORMAL_PRIORITY_CLASS;
                if (!NativeMethods.CreateProcessAsUser(userToken, exePath, null,
                        IntPtr.Zero, IntPtr.Zero, false, flags, env, null, ref si, out var pi))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessAsUser failed.");

                NativeMethods.CloseHandle(pi.hProcess);
                NativeMethods.CloseHandle(pi.hThread);
            }
            finally
            {
                if (env != IntPtr.Zero) NativeMethods.DestroyEnvironmentBlock(env);
            }
        }
        finally { NativeMethods.CloseHandle(userToken); }
    }

    private static class NativeMethods
    {
        public const uint NORMAL_PRIORITY_CLASS    = 0x00000020;
        public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

        [DllImport("kernel32.dll")] public static extern uint WTSGetActiveConsoleSessionId();
        [DllImport("Wtsapi32.dll", SetLastError = true)] public static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);
        [DllImport("userenv.dll",  SetLastError = true)] public static extern bool CreateEnvironmentBlock(out IntPtr env, IntPtr token, bool inherit);
        [DllImport("userenv.dll",  SetLastError = true)] public static extern bool DestroyEnvironmentBlock(IntPtr env);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CloseHandle(IntPtr handle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CreateProcessAsUser(
            IntPtr hToken, string? lpApplicationName, string? lpCommandLine,
            IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles,
            uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct STARTUPINFO
        {
            public int cb;
            public string? lpReserved, lpDesktop, lpTitle;
            public uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_INFORMATION
        {
            public IntPtr hProcess, hThread;
            public uint dwProcessId, dwThreadId;
        }
    }
}
