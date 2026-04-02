using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventLog;
using Screamsaver.Core;
using Screamsaver.Core.Ipc;

namespace Screamsaver.TrayApp;

static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    static void Main()
    {
        _mutex = new Mutex(true, "Screamsaver.TrayApp.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            return; // Already running
        }

        // SMELL-2: give the TrayApp the same structured-logging story as the Service.
        // Both processes write to the Windows Event Log under the "Screamsaver" source.
        // The source is registered by the MSI; in dev it may not exist — AddEventLog
        // silently falls back to no-op in that case.
        using var loggerFactory = LoggerFactory.Create(b =>
            b.AddEventLog(new EventLogSettings { SourceName = "Screamsaver" }));

        var repo = new SettingsRepository(
            new WindowsRegistryStore(Constants.RegistryKeyPath),
            loggerFactory.CreateLogger<SettingsRepository>());

        try
        {
            ApplicationConfiguration.Initialize();
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            // Construct the three collaborators that TrayApplicationContext depends on (DI-7).
            // PipeListener is constructed here (before TrayApplicationContext) and wired up
            // inside the constructor via its BlackoutReceived event.
            var overlayManager = new OverlayManager(
                repo,
                loggerFactory.CreateLogger<OverlayManager>(),
                loggerFactory.CreateLogger<OverlayForm>());
            var pinRateLimiter = new PinRateLimiter();
            var pipeListener   = new PipeListener(loggerFactory.CreateLogger<PipeListener>());

            Application.Run(new TrayApplicationContext(
                repo,
                new DefaultPipeClient(loggerFactory.CreateLogger<DefaultPipeClient>()),
                overlayManager,
                pipeListener,
                pinRateLimiter,
                loggerFactory
            ));
        }
        finally
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }
}
