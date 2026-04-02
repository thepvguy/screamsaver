using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Screamsaver.Core;
using Screamsaver.Core.Ipc;

namespace Screamsaver.TrayApp;

/// <summary>
/// Listens on the overlay named pipe for BLACKOUT commands from the service.
/// The pipe ACL restricts connections to LocalSystem and Administrators only,
/// preventing unprivileged processes from triggering the overlay.
///
/// Threading contract: <see cref="BlackoutReceived"/> is raised on the background
/// <c>Task.Run</c> thread managed by this class.  Subscribers must marshal to the
/// UI thread themselves (e.g. via <c>SynchronizationContext.Post</c>).
/// <see cref="Start"/> and <see cref="Stop"/> may be called from any thread.
/// </summary>
public class PipeListener
{
    /// <summary>
    /// Raised on the background listen thread when a BLACKOUT command is received.
    /// Subscribers are responsible for marshalling to the UI thread.
    /// </summary>
    public event Action? BlackoutReceived;

    private readonly ILogger<PipeListener> _logger;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public PipeListener(ILogger<PipeListener>? logger = null)
    {
        _logger = logger ?? NullLogger<PipeListener>.Instance;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listenTask = Task.Run(() => ListenLoop(_cts.Token));
    }

    /// <summary>
    /// Signals the listen loop to stop. Does not block — the loop observes the
    /// CancellationToken and exits after its current wait completes. Blocking here
    /// would freeze the UI thread for up to the pipe connect timeout (BUG-5).
    /// </summary>
    public void Stop() => _cts?.Cancel();

    private static NamedPipeServerStream CreateOverlayPipe()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            WindowsIdentity.GetCurrent().User!,
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            Constants.OverlayPipeName,
            PipeDirection.In,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Message,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            security);
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var pipe = CreateOverlayPipe();
                await pipe.WaitForConnectionAsync(ct);
                using var reader  = new StreamReader(pipe);
                var message = await reader.ReadToEndAsync(ct);

                if (message == PipeMessages.Blackout)
                    BlackoutReceived?.Invoke();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "[PipeListener] Listen loop error."); }
        }
    }
}
