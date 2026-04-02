using Screamsaver.Core;
using Screamsaver.Core.Ipc;

namespace Screamsaver.Service;

public class Worker : BackgroundService
{
    private readonly IAudioMonitor _audio;
    private readonly ITrayWatchdog _watchdog;
    private readonly IPipeServer   _pipeServer;
    private readonly IPipeClient   _pipeClient;
    private readonly ILogger<Worker> _logger;

    public Worker(IAudioMonitor audio, ITrayWatchdog watchdog, IPipeServer pipeServer,
                  IPipeClient pipeClient, ILogger<Worker> logger)
    {
        _audio      = audio;
        _watchdog   = watchdog;
        _pipeServer = pipeServer;
        _pipeClient = pipeClient;
        _logger     = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Screamsaver service starting.");

        _audio.ThresholdExceeded += OnThresholdExceeded;
        // RULE-13: PipeServer loads persisted pause state in its constructor; skip starting
        // audio if monitoring was paused before the last service restart.
        if (!_pipeServer.IsPaused)
            _audio.Start();

        // Run watchdog and pipe server with automatic restart on transient faults.
        // A bug in either component must NOT stop audio monitoring — that is the
        // core feature of the service (BUG-2).
        var watchdogTask = RunWithRestartAsync(_watchdog.RunAsync, stoppingToken, "TrayWatchdog");
        var pipeTask     = RunWithRestartAsync(_pipeServer.RunAsync, stoppingToken, "PipeServer");

        // Wait only for the service shutdown signal — faults in background tasks do not wake this.
        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        _audio.ThresholdExceeded -= OnThresholdExceeded;
        _audio.Stop();
        _logger.LogInformation("Screamsaver service stopped.");

        // Allow background tasks to finish cleanly (they observe stoppingToken).
        try { await Task.WhenAll(watchdogTask, pipeTask); }
        catch (OperationCanceledException) { }
    }

    /// <summary>Restarts <paramref name="run"/> indefinitely until cancellation.</summary>
    private async Task RunWithRestartAsync(
        Func<CancellationToken, Task> run, CancellationToken ct, string name)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await run(ct);
                return; // completed normally (cancellation handled internally)
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Component} faulted — restarting in 5 s.", name);
                await Task.Delay(TimeSpan.FromSeconds(5), ct)
                          .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    internal void OnThresholdExceeded(object? sender, EventArgs e)
    {
        _logger.LogInformation("Threshold exceeded — sending BLACKOUT.");
        _ = _pipeClient.SendAsync(Core.Constants.OverlayPipeName, PipeMessages.Blackout);
    }
}
