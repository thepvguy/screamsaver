# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Screamsaver** is a Windows parental-control application. A background Windows Service monitors the microphone; when the volume exceeds a threshold (child yelling), it sends a command over a named pipe to the tray app, which blacks out all monitors with a configurable fade effect. A PIN-protected system tray UI lets the parent control settings. The app is hardened against a child disabling or uninstalling it.

All projects target `net8.0-windows`.

## Solution Structure

```
Screamsaver.sln
├── Screamsaver.Core/               # Shared: AppSettings, IPC contracts, SettingsRepository, PinValidator, RecoveryPassword
├── Screamsaver.Service/            # Windows Service (LocalSystem): AudioMonitor, TrayWatchdog, PipeServer, Worker
├── Screamsaver.TrayApp/            # WinForms tray app: OverlayForm/Manager, SettingsForm, PipeListener, PinPromptForm
├── Screamsaver.UninstallHelper/    # Small WinForms exe called by the MSI CA to PIN-gate uninstall
└── Screamsaver.Installer/          # WiX v6 MSI: Package.wxs, Service.wxs, TrayApp.wxs, Registry.wxs, Build.ps1
```

## IPC Design

- **Service → TrayApp**: Named pipe `screamsaver-overlay` — service sends `"BLACKOUT"`
- **TrayApp → Service**: Named pipe `screamsaver-control` — tray sends `"PIN:<pin>|<command>"`
- Commands: `PAUSE`, `RESUME`, `UPDATE_SETTINGS:<json>`
- Both pipes are implemented with `NamedPipeServerStream` / `NamedPipeClientStream` (`System.IO.Pipes`)
- `PipeClient.SendAsync` in `Screamsaver.Core/Ipc/PipeClient.cs` is the shared send helper

## Settings & PIN

- All settings live in `HKLM\SOFTWARE\Screamsaver` (read/write by `SettingsRepository.cs` in Core)
- The PIN is stored as a BCrypt hash in the `PinHash` registry value (`PinValidator.cs`)
- A compiled-in recovery password lives in `RecoveryPassword.cs` (XOR-obfuscated byte arrays — change before shipping)
- `PinValidator.Verify()` accepts either the user PIN or the recovery password

## Common Commands

```bash
# Build entire solution
dotnet build

# Build a specific project
dotnet build Screamsaver.Service/Screamsaver.Service.csproj

# Publish for release (required before building the MSI)
dotnet publish Screamsaver.Service -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
dotnet publish Screamsaver.TrayApp -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
dotnet publish Screamsaver.UninstallHelper -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

# Build the MSI (publishes all projects then runs wix build)
powershell -ExecutionPolicy Bypass -File Screamsaver.Installer/Build.ps1

# Install WiX toolset (one-time)
dotnet tool install --global wix

# Run tests
dotnet test
```

## Development Workflow (No Installer Needed)

1. Run `Screamsaver.Service` as a console app (no `sc` registration needed in debug mode — `UseWindowsService()` handles dual-mode)
2. Run `Screamsaver.TrayApp` from the IDE simultaneously
3. The service listens on the named pipes; the tray app connects automatically

To register the service locally for testing:
```bash
sc create ScreamsaverService binPath="<path>\Screamsaver.Service.exe" start=auto
sc start ScreamsaverService
```

## Windows Service Notes

- `UseWindowsService()` in `Program.cs` auto-detects whether it's running as a service or console app
- The service runs as `LocalSystem` — it has full microphone access but cannot show UI
- `TrayWatchdog.cs` uses P/Invoke (`WTSQueryUserToken` + `CreateProcessAsUser`) to spawn the tray app into the interactive user session from Session 0

## Overlay System

- `OverlayForm.cs` creates one topmost `FormBorderStyle.None` window per `Screen.AllScreens` entry
- Extended style `WS_EX_TOOLWINDOW` excludes it from Alt+Tab
- Three phases driven by a 16ms `System.Windows.Forms.Timer`: fade-in → hold at MaxOpacity → fade-out → close
- Settings that control the overlay: `FadeInDurationMs`, `HoldDurationMs`, `FadeOutDurationMs`, `MaxOpacity`, `OverlayColor` (hex), `OverlayImagePath`
- `HoldDurationMs = 0` (default): overlay skips the hold phase and fades out immediately after reaching `MaxOpacity`

## Installer

- WiX v6 (`wix` CLI tool) — **not** MSBuild/`.wixproj` based
- Build via `Screamsaver.Installer/Build.ps1` (publishes projects, then calls `wix build`)
- The MSI embeds a `CustomAction` that calls `Screamsaver.UninstallHelper.exe` before uninstall; if the PIN is wrong the CA returns non-zero and the MSI aborts
- Registry ACLs (Users = read-only on `HKLM\SOFTWARE\Screamsaver`) are applied via an `icacls` VBScript custom action
- The tray app is set to auto-start via `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` (requires admin to remove)

## Hardening Notes

- Service failure actions: restart 3× with 3s delay (configured in `Service.wxs` via `util:ServiceConfig`)
- Standard users cannot stop the service (`sc stop` requires admin)
- `TrayWatchdog` relaunches the tray app if killed, every 5 seconds
- All sensitive tray app actions (Settings, Pause, Exit) require PIN
- Recovery password (default `ScreamsaverAdmin1!`) is XOR-obfuscated in `RecoveryPassword.cs` — **change before shipping** using `RecoveryPassword.GenerateArrays()` in DEBUG mode

## Coding Rules (pre-commit checklist)

These rules are derived from defects found in this codebase. Full rationale and the defects they prevent are in `issues.md`. Treat this list as a pre-commit checklist for the categories of work they cover.

### RULE-8 — Never silently swallow exceptions at security-critical decision points

**Applies to:** any catch block in authentication, authorisation, or PIN verification code.

A bare `catch { return false; }` converts every unexpected exception — corrupted data, library fault, invalid hash format — into a silent authentication failure. The correct pattern is to catch, log at error/warning level with the exception, and then return false:

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "BCrypt.Verify threw unexpectedly for stored hash.");
    return false;
}
```

*Defects this would have prevented: SMELL-F, SMELL-K.*

---

### RULE-9 — Never make a logger optional at a security-critical call site

**Applies to:** any method that catches exceptions at an authentication or credential-verification boundary.

Making a logger optional (`ILogger? logger = null`) lets callers silently opt out of diagnostics at compile time — no warning, no test failure. The correct approach:

1. **Make the logger required.** If the caller doesn't have one, fix the caller's constructor to inject one.
2. **Propagate the exception** to the nearest boundary that has a logger instead of catching locally.
3. Callers without DI infrastructure (e.g., CLI tools) must pass `NullLogger.Instance` **explicitly** — making the no-logging decision visible in code review.

Optional parameters are appropriate for `CancellationToken ct = default`. They are not appropriate when omitting them silently degrades observability at a security boundary.

*Defects this would have prevented: SMELL-G.*

---

### RULE-10 — `Dispose()` must replicate `Stop()` ordering, not bypass it

**Applies to:** any component whose `Stop()` method enforces a callback-quiescence sequence (RULE-1).

`Dispose()` is called by the DI container, by `using` blocks, and by tests — often without a preceding `Stop()`. If `Dispose()` goes straight to releasing handles without first setting guard flags and quiescing callbacks, in-flight callbacks may access disposed state and throw `ObjectDisposedException` or `NullReferenceException`. The correct pattern is:

1. Call `Stop()` from inside `Dispose(bool disposing)`, or inline the exact same ordering.
2. Guard with `if (_running)` so a `Stop()` → `Dispose()` sequence doesn't stop twice.

*Defects this would have prevented: BUG-E.*

---

### RULE-11 — Never call format-parsing functions on external data inside a constructor without exception handling

**Applies to:** constructors that parse registry values, config files, or any data from outside the process.

`Convert.FromHexString`, `int.Parse`, `Guid.Parse`, and similar functions throw on malformed input. If called in a constructor (or in a method called from a constructor), an unhandled exception propagates through DI container initialization and crashes the host — with no recovery path short of manual registry or file editing. The correct pattern is to catch parsing exceptions in the method that reads external data, log the error, and substitute a safe default:

```csharp
try { _cachedPinHmacKey = Convert.FromHexString(creds.PinHmacKey); }
catch (FormatException ex)
{
    _logger.LogError(ex, "PinHmacKey is corrupt — falling back to recovery-only mode.");
    _cachedPinHmacKey = [];
}
```

*Defects this would have prevented: ARCH-E.*

---

### RULE-12 — Security-critical byte layouts must have a single authoritative source

**Applies to:** any code that constructs the byte sequence fed to a MAC, hash, or signature function.

If the byte layout (field order, encoding, separators) is duplicated between the signer and the verifier, a future change to one side silently invalidates all authentication — every message returns NACK with no compile error and no warning. The canonical layout must live in exactly one function (e.g., `HmacAuth.BuildInputBytes`), and both sides must call it. Duplication is never acceptable here, even if "it's only a few lines".

*Defects this would have prevented: DUP-A.*

---

### RULE-13 — Mutable transient state that gates core functionality must be persisted

**Applies to:** any boolean or enum that enables or disables the primary feature of the service.

In-memory flags reset on process restart. If a parent pauses monitoring and the service crashes, the child gets a full reset of the enforcement. Any flag whose loss would surprise a user and degrade security must be written to the registry (or equivalent durable store) immediately when it changes, and loaded on startup. Document explicitly in the class whether each piece of state is persisted or ephemeral.

*Defects this would have prevented: ARCH-F.*

---

### RULE-14 — Multi-field credential records must be written atomically or with safe partial-failure fallback

**Applies to:** any multi-value credential write (PIN hash + HMAC key + HMAC salt, or similar tuples).

Writing N values as N separate calls means any failure between calls leaves the store in a split state. For credential records, a split state can make all future authentication fail — the hash says one PIN, the key says another. At a minimum: write in dependency order (PinHmacSalt → PinHmacKey → PinHash — the service reads Salt+Key first for pipe auth, PinHash is only used by the tray UI), document the failure modes for each intermediate state, and ensure the recovery password can still unlock the system regardless of which fields are stale.

*Defects this would have prevented: ARCH-G.*

---

### RULE-15 — Every named pipe server loop must have a per-connection read timeout

**Applies to:** any `NamedPipeServerStream` or similar server that reads from untrusted clients.

`ReadLineAsync(ct)` with only the global `CancellationToken` blocks indefinitely if a client connects and never sends data. One such client starves all subsequent legitimate clients for the lifetime of the service. Use a per-connection `CancellationTokenSource` linked to the outer token with a bounded timeout (e.g., 5 seconds), and cancel it regardless of success or failure when the connection is done.

*Defects this would have prevented: ARCH-H.*

---

### RULE-16 — Catch the specific exception type, never use bare `catch { }` at security decision points

**Applies to:** any `try/catch` inside authentication, authorisation, credential parsing, or protocol message parsing.

A bare `catch { }` catches *all* exceptions including `OutOfMemoryException` and `StackOverflowException`. At a security boundary that returns `false` or `NACK`, this converts a process-health crisis into a silent authentication failure. Always name the narrowest expected exception type:

```csharp
// Wrong:
try { received = Convert.FromHexString(hmacHex); }
catch { return false; }

// Right:
try { received = Convert.FromHexString(hmacHex); }
catch (FormatException ex)
{
    _logger.LogWarning(ex, "Malformed HMAC hex from client.");
    return false;
}
```

Naming the exception type also documents the data contract at the call site: a future library change that stops throwing that type becomes a visible compile or test failure rather than a silent behaviour change. This rule is a corollary of RULE-8.

*Defects this would have prevented: SMELL-K.*

---

### RULE-17 — Guard the loop-break on the global `CancellationToken`; never use bare `catch (OperationCanceledException) { break; }` in a loop with per-iteration linked tokens

**Applies to:** any loop that creates a per-iteration `CancellationTokenSource.CreateLinkedTokenSource(ct)` alongside a global shutdown token.

`OperationCanceledException` carries the token that triggered it. A bare `catch (OperationCanceledException) { break; }` exits the loop for *any* cancellation, including per-iteration timeouts. This turns a single slow/silent client into a permanent shutdown of the server loop. Guard the break:

```csharp
// Wrong — a per-connection timeout kills the whole server:
catch (OperationCanceledException) { break; }

// Right — only global shutdown exits the loop:
catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
```

**Ownership corollary:** every `CancellationTokenSource` created with `CreateLinkedTokenSource` inside a loop iteration owns a *per-iteration* scope. Name it to make that scope explicit (`perConnCts`, not `cts`), and document in the catch which tokens each handler is responsible for. Ambiguous token ownership in catch clauses is a sign that the cancellation model has not been fully designed.

*Defects this would have prevented: BUG-F.**

---

### RULE-18 — When a `catch` clause is guarded by `when`, trace every exception that no longer matches the guard to its new landing point and verify the severity is correct

**Applies to:** any loop that uses a guarded catch clause (e.g., `when (ct.IsCancellationRequested)`) to discriminate between two or more exception sources.

Adding a `when` guard changes what a catch clause handles — and also what it *does not* handle. Every exception that previously matched and now falls through must be traced to its new landing point. A generic `catch (Exception ex) { _logger.LogError(...) }` is often the fallback; if an expected operational exception (like a per-connection timeout) lands there, it is logged as an error when it is normal behaviour.

**Audit checklist when adding a `when` guard:**
1. List every exception type and triggering condition the original unguarded catch handled.
2. For each: does the guard now let it fall through? If so, where does it land?
3. Is the new landing point's log level, propagation, and loop-control action appropriate?
4. If not, add an explicit catch clause (or branch) before the generic handler.

```csharp
// Wrong — per-connection OCE falls to generic ERROR handler after the guard is added:
catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
catch (Exception ex) { _logger.LogError(ex, "PipeServer error."); }

// Right — add an explicit clause for the per-connection timeout case:
catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
catch (OperationCanceledException)
{
    _logger.LogWarning("Per-connection read timeout — client connected but sent no data.");
    // loop continues
}
catch (Exception ex) { _logger.LogError(ex, "Unexpected PipeServer error."); }
```

This rule is the complement to RULE-17: RULE-17 prevents a per-iteration timeout from exiting the loop; RULE-18 prevents it from being logged at the wrong severity after the loop exit is guarded.

*Defects this would have prevented: SMELL-M.*

---

### RULE-19 — Any catch block that handles user-configuration failures must either log or propagate; silent `return false` is not acceptable when a logger is reachable

**Applies to:** any method that evaluates external configuration (registry values, user-supplied paths, settings strings) and returns a boolean or default to signal rejection.

RULE-8 covers authentication boundaries. This rule covers configuration decision points: when code rejects a user-supplied value (invalid path, unreadable image, malformed color), the rejection must be diagnosable. A silent `return false` means operators see "feature not working" with no trace of why.

The two acceptable patterns are:
1. **Log in the catching method** — requires an `ILogger` parameter (required, not optional per RULE-9) or access to one via the enclosing class.
2. **Propagate to a caller that has a logger** — let the exception reach the scope that owns context (which setting failed, what value was rejected).

```csharp
// Wrong: named types, but no log — operator sees nothing
catch (Exception ex) when (ex is ArgumentException or SecurityException ...)
{ return false; }

// Right: log with the path value so the operator can diagnose it
catch (Exception ex) when (ex is ArgumentException or SecurityException ...)
{
    logger.LogWarning(ex, "OverlayImagePath '{Path}' rejected — path is not safe.", path);
    return false;
}
```

**Corollary:** if you cannot log because no logger is reachable, the enclosing class is missing an `ILogger` constructor parameter — fix the class, not the helper. A `private static` method that validates user configuration and silently returns `false` is a strong signal that the class needs a logger.

*Defects this would have prevented: SMELL-O.*

---

### RULE-20 — Enum variants, docstrings, and CLAUDE.md descriptions must agree; divergence is a defect

**Applies to:** any type whose phase or state model is described in both documentation (class XML doc, CLAUDE.md) and code (enum, switch statement).

When a refactor adds, removes, or renames enum variants or state-machine transitions, update documentation in the same commit. A doc comment that describes more (or fewer) states than the enum contains is wrong. Future maintainers and LLMs will trust the documentation and be misled.

**Checklist when changing an enum or state machine:**
1. Update the enum declaration.
2. Update all `switch`/`if` logic that handles the states.
3. Update the class XML doc comment to reflect the new states and transitions.
4. Update CLAUDE.md if the state machine is documented there.
5. Add or update a test that exercises the full state sequence end-to-end.

**Doc-code divergence rule:** if documentation says "N phases" but the enum has fewer variants, fix the implementation to match the documented intent — or fix the documentation to match the implementation. Never leave them in disagreement.

*Defects this would have prevented: SMELL-P.*

---

### RULE-21 — Inject `ILogger<T>` directly when T is known at compile time; reserve `ILoggerFactory` for runtime-determined categories

**Applies to:** any class that injects a logging dependency.

Use `ILoggerFactory` only when the logger category type cannot be determined until runtime (e.g., generic components, dynamic type dispatch). When T is fixed at compile time, inject `ILogger<T>` directly:

| Scenario | Preferred injection |
|----------|---------------------|
| Class logs only for itself | `ILogger<Self>` in constructor |
| Class creates instances of one known child type and passes loggers to them | `ILogger<ChildType>` in constructor; store as field; pass to each child at construction |
| Class creates instances of multiple or truly runtime-determined types | `ILoggerFactory`; call `CreateLogger<T>()` in **constructor** (not in methods), one call per known category |

**Never call `CreateLogger<T>()` inside a method** — this makes it appear that a new logger is created on each invocation, hides the cached-by-category behaviour, and obscures the dependency in the constructor signature.

**A class that stores `ILoggerFactory` must also create its own `ILogger<Self>` in the constructor.** Omitting the self-logger while holding the factory leaves the class unable to report its own errors — a hidden observability gap.

*Defects this would have prevented: SMELL-Q.*

---

### RULE-22 — Never inject a dependency solely to satisfy a structural principle; only inject what you actually call

**Applies to:** any class where a dependency is added because "it should have one" rather than because existing code calls it.

An injected dependency with zero callers is dead code. It adds a required constructor parameter, forces tests to supply it, and creates confusion about whether it is intentionally unused or awaiting future use.

- Log statements first, logger injection second. Identify the specific call sites before adding the constructor parameter.
- If there is nothing to log, do not inject the logger. Simple pass-through classes with no error handling have nothing to report.
- If you add it for anticipated future use, add a `// TODO: log X` comment at the planned call site — don't leave a silent unused field.

The test smell that reveals a dead dependency: a mock is supplied in the test but never verified — it contributes nothing to the assertions.

*Defects this would have prevented: SMELL-R.*

---

### RULE-23 — Document and enforce the minimum effective value for parameters where integer truncation can silently nullify a feature

**Applies to:** any `(int)(userValue / granularity)` computation that drives a feature that is either on or off depending on whether it rounds to zero.

When a duration is divided by a tick granularity and truncated, any value in `[1, granularity - 1]` silently becomes zero — identical to "feature disabled". The gap between `userValue > 0` (necessary) and `userValue >= granularity` (sufficient) is invisible without documentation or enforcement.

**Required mitigations (choose at least one):**
1. **Enforce in `Validate()`** — clamp sub-granularity positives up to the granularity or down to zero, with a comment.
2. **Document on the property** — state the minimum effective positive value in the XML doc.
3. **Add a test at the production granularity** — test a sub-granularity positive value explicitly at the real tick rate, not just at an artificial round-number rate.

*Defects this would have prevented: ARCH-K.*

---

### RULE-24 — Add a resource to a tracking collection only after all post-construction operations that might fail have completed

**Applies to:** any code that constructs an object, adds it to a tracking list, then calls a method on it that can throw — where the exception is caught and the loop continues rather than propagating.

When catch-and-continue is present, a failed post-construction call leaves the resource in the collection in a partial state. If the cleanup predicate (`IsDisposed`, etc.) doesn't identify partially-initialized resources, they accumulate on every retry.

The two safe patterns:

```csharp
// Option A — add after the final success gate:
var form = new OverlayForm(settings, screen.Bounds, _overlayLogger);
form.Show();        // throws → nothing added
_active.Add(form);  // only reached on success

// Option B — remove and dispose on failure:
_active.Add(form);
try { form.Show(); }
catch (Exception ex)
{
    _active.Remove(form);
    form.Dispose();
    _logger.LogError(ex, "...");
}
```

**When adding catch-and-continue to an existing block**, re-audit every resource acquired before the try. Resources tracked before the failable operation may now escape cleanup — the same discipline as RULE-10 (Dispose must not bypass Stop).

*Defects this would have prevented: BUG-G.*

---

### RULE-25 — Shared data model libraries must not contain constants that describe the runtime behavior of one specific consuming subsystem

**Applies to:** any constant added to a shared library type (e.g., a Core data model) in order to enable validation logic that only makes sense for one consumer.

A constant that encodes an implementation detail of a single consumer — a timer interval, a rendering frame rate, a thread pool size — does not belong on a shared model type, even if placing it there is convenient for a `Validate()` method. The test: would *all* consumers be broken if this constant were wrong? If only one consumer is affected, the constant belongs in that consumer.

**Permitted in a shared `Validate()` method:** domain-neutral range constraints that hold for every consumer (non-negative durations, clamped opacity ratios). **Not permitted:** granularity rounding that depends on a specific consumer's runtime characteristics (e.g., a WinForms timer tick rate).

**The two clean alternatives:**

1. **Validate at the point of consumption.** Let `Validate()` enforce only domain-neutral bounds. The consuming component applies granularity rounding using its own local constant:
   ```csharp
   // In OverlayPhaseController — not in AppSettings.Validate()
   private const int TickMs = 16;
   _holdTicksRemaining = settings.HoldDurationMs > 0
       ? Math.Max(1, (int)(settings.HoldDurationMs / (double)TickMs))
       : 0;
   ```

2. **Pass the granularity as a parameter** to `Validate()` rather than hard-coding it on the shared type:
   ```csharp
   public AppSettings Validate(int holdGranularityMs = 0) => this with
   {
       HoldDurationMs = holdGranularityMs > 0 && HoldDurationMs > 0
           ? Math.Max(holdGranularityMs, HoldDurationMs)
           : Math.Max(0, HoldDurationMs),
       ...
   };
   ```

**Documentation corollary:** if a constant is intentionally placed in the shared model for cross-cutting reasons, its XML doc must state which consumers depend on it and what must happen if they diverge. Undocumented shared constants are assumed domain-neutral by future maintainers.

*Defects this would have prevented: ARCH-L.*

---

### RULE-26 — Tests that exist to exercise production-granularity behavior must reference the production constant, not a hardcoded copy of its value

**Applies to:** any test added specifically to cover a production-rate boundary (timer tick, buffer size, retry count, etc.) defined by a constant in production code.

If a test uses a magic number that happens to equal a production constant, it is invisibly decoupled from that constant. When the constant changes, the test silently continues to pass at the old value — no longer exercising the production boundary it was written to guard.

```csharp
// Wrong — silently tests the wrong rate if TickMs changes:
var ctrl = new OverlayPhaseController(settings, tickMs: 16);

// Right — stays in sync with production automatically:
var ctrl = new OverlayPhaseController(settings, tickMs: OverlayPhaseController.TickMs);
```

If the constant is `internal`, use `InternalsVisibleTo` (already in place for this project) to make it accessible. A test named `ProductionTickMs_*` that does not reference a production constant is self-contradicting.

*Defects this would have prevented: TEST-14.*

---

### RULE-27 — Documentation in a shared library must not hardcode a numeric value owned by a specific consuming assembly

**Applies to:** any XML doc comment or inline comment in a shared library that names a specific numeric value whose authoritative source lives in a consumer assembly.

When code coupling is removed (e.g., deleting a constant from Core), verify the same value has not been re-introduced as a magic number in a comment. Documentation coupling has the same maintenance risk as code coupling but is invisible to the compiler.

**Two acceptable alternatives:**

1. **Describe behavior without the value:** "values smaller than the rendering layer's timer granularity are rounded up" — correct regardless of what the granularity is.

2. **Add an explicit maintenance note** naming the owning constant and file: `// production interval is OverlayPhaseController.TickMs in Screamsaver.TrayApp — update this if that changes`.

**Checklist when removing a constant from a shared library:**
1. Delete or move the constant.
2. Grep the shared library's comments and XML docs for the constant's numeric value.
3. For each hit, replace with a behavior description or add a maintenance note.

*Defects this would have prevented: SMELL-U.*
