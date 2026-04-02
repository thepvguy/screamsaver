using Microsoft.Extensions.Logging;
using Screamsaver.Core.Models;
using Screamsaver.Core.Security;

namespace Screamsaver.UninstallHelper;

/// <summary>
/// Business logic for uninstall PIN verification, extracted from Program for testability.
/// No WinForms or registry dependency — all inputs are explicit parameters.
/// </summary>
internal static class UninstallLogic
{
    /// <param name="PinFromStdin">
    /// When true the caller reads the PIN from stdin instead of <see cref="Pin"/>.
    /// </param>
    /// <param name="PinFile">
    /// When set, the caller reads the PIN from this file path and then deletes it.
    /// This keeps the PIN off the process command line (SEC-C): cmd.exe writes the
    /// file and exits immediately; UninstallHelper only receives the file path.
    /// </param>
    internal sealed record ParsedArgs(
        bool    Silent,
        string? Pin,
        bool    PinFromStdin = false,
        string? PinFile      = null);

    internal static ParsedArgs ParseArgs(string[] args)
    {
        bool    silent       = false;
        bool    pinFromStdin = false;
        string? pin          = null;
        string? pinFile      = null;

        for (int i = 0; i < args.Length; i++)
        {
            if      (args[i] == "--silent")                          silent       = true;
            else if (args[i] == "--pin-stdin")                       pinFromStdin = true;
            else if (args[i] == "--pin"      && i + 1 < args.Length) pin      = args[++i];
            else if (args[i] == "--pin-file" && i + 1 < args.Length) pinFile  = args[++i];
        }
        return new ParsedArgs(silent, pin, pinFromStdin, pinFile);
    }

    /// <summary>
    /// Verifies a PIN during silent uninstall.
    /// Returns 0 if the PIN is correct, 1 if absent or wrong.
    /// <paramref name="logger"/> is required by <see cref="PinValidator.Verify"/> (RULE-9);
    /// callers without DI infrastructure must pass <c>NullLogger.Instance</c> explicitly.
    /// </summary>
    internal static int RunSilent(string? pin, PinCredentials credentials, ILogger logger)
    {
        if (string.IsNullOrEmpty(pin))
            return 1;

        return PinValidator.Verify(pin, credentials.PinHash, logger) ? 0 : 1;
    }

    /// <summary>
    /// Reads the PIN from <paramref name="path"/>, deletes the file, and returns the PIN.
    /// Returns <c>null</c> if the file cannot be read.  The delete is always attempted
    /// (even on read failure) so the file is not left on disk on any code path.
    /// </summary>
    internal static string? ReadAndDeletePinFile(string path)
    {
        string? contents = null;
        try   { contents = File.ReadAllText(path).Trim(); }
        catch { /* can't read — returns null, verification fails */ }
        finally
        {
            try { File.Delete(path); } catch { /* best-effort */ }
        }
        return contents;
    }
}
