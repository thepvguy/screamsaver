using Microsoft.Extensions.Logging;
using NSubstitute;
using Screamsaver.Core;
using Screamsaver.Core.Ipc;
using Screamsaver.Service;

namespace Screamsaver.Tests.Service;

/// <summary>
/// Tests for <see cref="Worker"/> business logic (TEST-2).
/// Uses NSubstitute mocks so no real audio capture or named pipes are required.
/// </summary>
public class WorkerTests
{
    private static Worker CreateWorker(
        IAudioMonitor? audio      = null,
        ITrayWatchdog? watchdog   = null,
        IPipeServer?   pipeServer = null,
        IPipeClient?   pipeClient = null)
    {
        audio      ??= Substitute.For<IAudioMonitor>();
        watchdog   ??= Substitute.For<ITrayWatchdog>();
        pipeServer ??= Substitute.For<IPipeServer>();
        pipeClient ??= Substitute.For<IPipeClient>();

        // Background tasks must honour cancellation so StopAsync completes cleanly.
        watchdog.RunAsync(Arg.Any<CancellationToken>())
            .Returns(ci => Task.Delay(Timeout.Infinite, ci.Arg<CancellationToken>()));
        pipeServer.RunAsync(Arg.Any<CancellationToken>())
            .Returns(ci => Task.Delay(Timeout.Infinite, ci.Arg<CancellationToken>()));

        return new Worker(audio, watchdog, pipeServer, pipeClient,
                          Substitute.For<ILogger<Worker>>());
    }

    // ── OnThresholdExceeded ───────────────────────────────────────────────────

    [Fact]
    public void OnThresholdExceeded_SendsBlackoutToOverlayPipe()
    {
        var pipeClient = Substitute.For<IPipeClient>();
        var worker = CreateWorker(pipeClient: pipeClient);

        worker.OnThresholdExceeded(null, EventArgs.Empty);

        pipeClient.Received(1).SendAsync(
            Constants.OverlayPipeName, PipeMessages.Blackout, Arg.Any<int>());
    }

    // ── ExecuteAsync lifecycle ────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_StartsAudio_OnStart()
    {
        var audio  = Substitute.For<IAudioMonitor>();
        var worker = CreateWorker(audio: audio);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);

        // audio.Start() is called synchronously before the first real await in ExecuteAsync
        audio.Received(1).Start();

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_StopsAudio_OnShutdown()
    {
        var audio  = Substitute.For<IAudioMonitor>();
        var worker = CreateWorker(audio: audio);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await worker.StopAsync(CancellationToken.None);

        audio.Received(1).Stop();
    }

    // ── Fault isolation (BUG-2) ───────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WatchdogFault_DoesNotStopAudio()
    {
        var audio      = Substitute.For<IAudioMonitor>();
        var watchdog   = Substitute.For<ITrayWatchdog>();
        var pipeServer = Substitute.For<IPipeServer>();
        var pipeClient = Substitute.For<IPipeClient>();

        // Watchdog faults immediately on every call (simulates repeated crashes)
        watchdog.RunAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("watchdog fault")));
        pipeServer.RunAsync(Arg.Any<CancellationToken>())
            .Returns(ci => Task.Delay(Timeout.Infinite, ci.Arg<CancellationToken>()));

        var worker = new Worker(audio, watchdog, pipeServer, pipeClient,
                                Substitute.For<ILogger<Worker>>());

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        // Give RunWithRestartAsync time to process the fault and enter the 5 s back-off
        await Task.Delay(50);

        // Audio must still be running — a watchdog fault must NOT stop audio monitoring
        audio.DidNotReceive().Stop();

        await worker.StopAsync(CancellationToken.None);
        audio.Received(1).Stop(); // stop happens only on service shutdown
    }

    [Fact]
    public async Task ExecuteAsync_PipeServerFault_DoesNotStopAudio()
    {
        var audio      = Substitute.For<IAudioMonitor>();
        var watchdog   = Substitute.For<ITrayWatchdog>();
        var pipeServer = Substitute.For<IPipeServer>();
        var pipeClient = Substitute.For<IPipeClient>();

        watchdog.RunAsync(Arg.Any<CancellationToken>())
            .Returns(ci => Task.Delay(Timeout.Infinite, ci.Arg<CancellationToken>()));
        pipeServer.RunAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("pipe fault")));

        var worker = new Worker(audio, watchdog, pipeServer, pipeClient,
                                Substitute.For<ILogger<Worker>>());

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await Task.Delay(50);

        audio.DidNotReceive().Stop();

        await worker.StopAsync(CancellationToken.None);
        audio.Received(1).Stop();
    }
}
