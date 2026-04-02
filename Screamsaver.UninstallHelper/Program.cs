using Microsoft.Extensions.Logging.Abstractions;
using Screamsaver.Core;
using Screamsaver.UninstallHelper;
using Screamsaver.WinForms;

namespace Screamsaver.UninstallHelper;

/// <summary>
/// Invoked by the MSI uninstall custom action.
/// Exits with code 0 if the PIN is correct, 1 if wrong or cancelled.
/// The MSI custom action uses Return="check" so a non-zero exit aborts uninstall.
///
/// Modes:
///   (no args)                    — show interactive PIN dialog (normal uninstall)
///   --silent                     — no PIN provided in quiet mode; block immediately
///   --silent --pin VALUE         — verify VALUE (PIN visible on this process's command line)
///   --silent --pin-stdin         — read PIN from stdin (PIN in parent process's args)
///   --silent --pin-file PATH     — read PIN from file at PATH and delete it immediately;
///                                  the MSI writes the file via a separate cmd.exe invocation
///                                  so the PIN never appears on this process's command line
///                                  (SEC-C mitigation — see Package.wxs WritePinToFile CA)
/// </summary>
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var opts = UninstallLogic.ParseArgs(args);

        if (opts.Silent)
        {
            string? pin;
            if (opts.PinFile is not null)
                pin = UninstallLogic.ReadAndDeletePinFile(opts.PinFile);
            else if (opts.PinFromStdin)
                pin = Console.ReadLine()?.Trim();
            else
                pin = opts.Pin;

            // RULE-9: PinValidator.Verify requires a logger. UninstallHelper has no DI
            // infrastructure, so pass NullLogger explicitly (no-op, but visible in code review).
            return UninstallLogic.RunSilent(pin, SettingsRepository.Instance.LoadCredentials(),
                NullLogger.Instance);
        }

        // Interactive mode: show the PIN dialog.
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        using var prompt = new PinPromptForm("Enter PIN to uninstall Screamsaver:");
        if (prompt.ShowDialog() != DialogResult.OK)
            return 1;

        var credentials = SettingsRepository.Instance.LoadCredentials();
        return UninstallLogic.RunSilent(prompt.EnteredPin, credentials,
            NullLogger.Instance);
    }
}
