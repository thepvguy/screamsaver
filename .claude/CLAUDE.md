# Codebase Navigation — Screamsaver

LLM-optimised reference. Not kept in sync manually — verify against source before acting on specifics.
Known open issues: `issues.md` at repo root.

---

## Process topology

```
[Windows Service: Screamsaver.Service]  (LocalSystem, Session 0, no UI)
       │  pipe: screamsaver-overlay  →  sends "BLACKOUT"
       │  pipe: screamsaver-control  ←  receives "HMAC:...|COMMAND"
       ↓
[WinForms TrayApp: Screamsaver.TrayApp]  (interactive user session)
```

Service spawns TrayApp via `CreateProcessAsUser` (Session 0 → interactive session).
TrayApp is single-instance (named Mutex). Service watchdog relaunches it every 5 s if killed.

---

## Solution map

### Screamsaver.Core  _(shared library, no UI, no WinForms)_

| File | Purpose | Key symbols |
|------|---------|-------------|
| `Constants.cs` | Pipe names, registry path, service/exe names | `OverlayPipeName`, `ControlPipeName`, `RegistryKeyPath` |
| `ISettingsRepository.cs` | Persistence contract | `Load()`, `Save()`, `LoadCredentials()`, `SaveCredentials()` |
| `SettingsRepository.cs` | HKLM registry impl; optional `ILogger` ctor param | `Instance` (singleton for DI-less callers) |
| `IRegistryStore.cs` / `WindowsRegistryStore.cs` | Registry abstraction (tests use `MemoryRegistryStore`) | `GetString/SetString`, `GetInt/SetInt`, `GetDouble/SetDouble` |
| `IAudioMonitor.cs` | Audio monitoring contract | `ThresholdExceeded` event, `Start()`, `Stop()`, `UpdateSettings()` |
| `IPipeServer.cs` | Pipe server contract | `RunAsync(CancellationToken)` |
| `ITrayWatchdog.cs` | Watchdog contract | `RunAsync(CancellationToken)` |
| `AudioLevelCalculator.cs` | Pure RMS/dBFS maths; no state | `ComputeRmsFloat32`, `ComputeRmsPcm16`, `ToDbFs` |
| `Models/AppSettings.cs` | Immutable settings record | `Validate()` clamps ranges before registry write |
| `Models/PinCredentials.cs` | Immutable credential record | `PinHash` (BCrypt), `PinHmacKey` (PBKDF2 hex), `PinHmacSalt` (hex); `IsConfigured` |
| `Models/SettingsUpdatePayload.cs` | Pipe payload for UPDATE_SETTINGS | `Settings`, `Credentials?` |
| `Ipc/IPipeClient.cs` | Client contract | `SendAsync` (one-way), `SendControlAsync` (auth round-trip) |
| `Ipc/DefaultPipeClient.cs` | Production pipe client; `ILogger` ctor param | Full PBKDF2+HMAC handshake; `Task.Run` for key derivation |
| `Ipc/ControlResult.cs` | ACK result | `sealed record(bool Success, bool? ServiceIsPaused)`; `Nack` static |
| `Ipc/PipeMessages.cs` | Command string constants | `Blackout`, `Pause`, `Resume`, `UpdateSettingsPrefix`, helpers |
| `Ipc/HmacAuth.cs` | **internal** — HMAC key derivation + message building | `DeriveKey(pin,salt)` PBKDF2-SHA256 100K iters; `BuildMessage(key,nonce,cmd)` HMAC over `nonce‖UTF8(cmd)` |
| `Security/PinValidator.cs` | BCrypt verify + PBKDF2 credential derivation | `MinimumPinLength=4`; `HashPin`, `Verify`, `DeriveHmacCredentials` |
| `Security/RecoveryPassword.cs` | XOR-obfuscated fallback password | `Get()`, `Verify()` (constant-time); `GenerateArrays` (#if DEBUG only) |

`InternalsVisibleTo`: `Screamsaver.Tests` and `Screamsaver.Service` (needed for `HmacAuth`).

---

### Screamsaver.Service  _(Windows Service / console dual-mode)_

| File | Purpose | Key symbols |
|------|---------|-------------|
| `Program.cs` | Host builder; DI wiring; `UseWindowsService()` | All singletons; `AddWindowsService` |
| `Worker.cs` | `BackgroundService`; wires audio+watchdog+pipe | `ExecuteAsync`; `RunWithRestartAsync` (restart on fault without stopping audio); `OnThresholdExceeded` (internal, tested) |
| `AudioMonitor.cs` | WASAPI capture; threshold/cooldown state machine | `Func<IWaveIn>` factory (testable); `internal OnDataAvailable`; threading contract in class doc |
| `PipeServer.cs` | Named pipe server; HMAC auth; rate-limiting | `internal async Task<string> ProcessMessage` (tested); credential key cache; `RefreshKeyCache` in `Task.Run` |
| `TrayWatchdog.cs` | P/Invoke `WTSQueryUserToken`+`CreateProcessAsUser`; 5 s poll | `internal static IsCorrectInstance` (tested) |

**DI registrations** (all singletons): `ISettingsRepository→SettingsRepository`, `IPipeClient→DefaultPipeClient`, `IAudioMonitor→AudioMonitor`, `ITrayWatchdog→TrayWatchdog`, `IPipeServer→PipeServer`, `Worker` as hosted service.

---

### Screamsaver.TrayApp  _(WinForms; single-instance via named Mutex)_

| File | Purpose | Key symbols |
|------|---------|-------------|
| `Program.cs` | Entry point; `LoggerFactory` (EventLog sink); DI-free wiring | Creates `SettingsRepository` with logger; passes `ILoggerFactory` to `TrayApplicationContext` |
| `TrayApplicationContext.cs` | `ApplicationContext`; tray icon + context menu; PIN auth for all actions | `OnPauseResume` (async void, sends pipe command); `PromptPin` helper; `_paused` synced from `result.ServiceIsPaused` |
| `PipeListener.cs` | Listens on overlay pipe for BLACKOUT; background `Task.Run` loop | `ILogger` ctor param; `Stop()` is non-blocking (cancel only) |
| `OverlayManager.cs` | Creates one `OverlayForm` per `Screen.AllScreens` | `ShowOverlay(AppSettings?)`; injects `ILogger<OverlayManager>` + `ILogger<OverlayForm>` |
| `OverlayForm.cs` | Topmost borderless window; delegates phase logic to `OverlayPhaseController` | `WS_EX_TOOLWINDOW` (hides from Alt+Tab); fade-in → hold → fade-out; `HoldDurationMs=0` skips hold |
| `OverlayPhaseController.cs` | Pure state machine for 3-phase overlay — no WinForms dependency | `Tick()`, `Opacity`, `IsComplete`; fully unit-tested in `TrayApp/OverlayPhaseControllerTests.cs` |
| `SettingsForm.cs` | Settings UI; PIN change; preview; save via pipe | `_pendingCredentials` (set by `OnChangePin`, sent by `OnSave`); `Task.Run` for BCrypt+PBKDF2 |
| `PinRateLimiter.cs` | UI-side lockout: 5 fails → 5 min | Thread-safe (`_lock`); `IsLockedOut`, `LockoutRemaining`, `RecordFailure`, `RecordSuccess` |
| `HelpForm.cs` / `HelpText.cs` | Static help dialog | No logic |
| `UiHelpers.cs` | `ParseColor(hex)` | |

---

### Screamsaver.WinForms  _(shared WinForms controls)_

| File | Purpose |
|------|---------|
| `PinPromptForm.cs` | Generic PIN entry dialog; `EnteredPin` property; `DialogResult.OK` on confirm |

---

### Screamsaver.UninstallHelper  _(small exe called by MSI CA)_

| File | Purpose | Key symbols |
|------|---------|-------------|
| `Program.cs` | Entry; `--silent --pin-file PATH` / `--silent --pin-stdin` / interactive | Exits 0=OK, 1=wrong/cancelled |
| `UninstallLogic.cs` | PIN verify + `sc stop/delete`; `ParseArgs`; `ReadAndDeletePinFile` | |

---

### Screamsaver.Installer  _(WiX v6, not MSBuild)_

| File | Purpose |
|------|---------|
| `Package.wxs` | MSI shell; `WritePinToFile` + `VerifySilentUninstallPin` CAs; `SCREAMSAVER_PIN Hidden="yes"` |
| `Service.wxs` | Service install; failure restart × 3 @ 3 s via `util:ServiceConfig` |
| `TrayApp.wxs` | TrayApp files; `HKLM\...\Run` autostart |
| `Registry.wxs` | `HKLM\SOFTWARE\Screamsaver` key; ACL: Users=read-only via `util:PermissionEx` |
| `Build.ps1` | Publishes all three exes then runs `wix build` |

---

### Screamsaver.Tests

```
Core/
  AppSettingsTests.cs          AppSettings.Validate() clamping
  AudioLevelCalculatorTests.cs RMS/dBFS maths
  PinCredentialsTests.cs       IsConfigured logic
  PinValidatorTests.cs         BCrypt hash+verify, DeriveHmacCredentials
  PipeMessagesTests.cs         Command string helpers
  RecoveryPasswordTests.cs     XOR decode, Verify
  SettingsRepositoryTests.cs   Load/Save round-trips via MemoryRegistryStore
Service/
  PipeServerTests.cs           ProcessMessage: ACK/NACK, rate-limit, UpdateSettings clamping
  TrayWatchdogTests.cs         IsCorrectInstance path matching
  WorkerTests.cs               OnThresholdExceeded, audio lifecycle, fault isolation
UninstallHelper/
  ProgramTests.cs              ParseArgs, RunSilent, ReadAndDeletePinFile
```

**No tests for:** `AudioMonitor` (infrastructure ready: `Func<IWaveIn>`, internal `OnDataAvailable`), `TrayApplicationContext`, `OverlayManager`, `PipeListener`.

```
TrayApp/
  OverlayPhaseControllerTests.cs  OverlayPhaseController: full 3-phase state machine (11 tests)
```

---

## Control pipe protocol (per connection)

```
Server→Client  {nonce_hex:32}\n{salt_hex:32}\n
Client→Server  HMAC:{hmac_sha256_hex:64}|{command}\n
Server→Client  OK:PAUSED\n  |  OK:RUNNING\n  |  NACK\n
```

- `hmac_sha256` authenticates `nonce_bytes ‖ UTF8(command)` under `PBKDF2(pin, salt, 100000, SHA256, 32)`
- `salt` is `PinCredentials.PinHmacSalt`; server sends it so client can derive the same key
- Service pre-computes `_cachedPinHmacKey` at startup and on credential rotation
- Recovery password uses a fixed salt `HmacAuth.RecoverySalt` ("ScreamsaverRecov")
- Rate-limit: 5 failures → 30 s lockout (server-side, `PipeServer._rateLimitLock`)

Commands: `PAUSE`, `RESUME`, `UPDATE_SETTINGS:{SettingsUpdatePayload json}`

---

## Registry layout  (`HKLM\SOFTWARE\Screamsaver`)

| Value | Type | Notes |
|-------|------|-------|
| `ThresholdDb` | `REG_BINARY` (double) | Default -20.0 |
| `CooldownSeconds` | `REG_DWORD` | Default 30, min 1 |
| `FadeInDurationMs` | `REG_DWORD` | Default 0 |
| `HoldDurationMs` | `REG_DWORD` | Default 0 (skip hold phase) |
| `FadeOutDurationMs` | `REG_DWORD` | Default 5000 |
| `MaxOpacity` | `REG_BINARY` (double) | Default 1.0, clamped 0.01–1.0 |
| `OverlayColor` | `REG_SZ` | Hex e.g. `#000000` |
| `OverlayImagePath` | `REG_SZ` | Empty = no image |
| `PinHash` | `REG_SZ` | BCrypt hash, work factor 12 |
| `PinHmacKey` | `REG_SZ` | PBKDF2-derived key, uppercase hex |
| `PinHmacSalt` | `REG_SZ` | Random 16-byte salt, uppercase hex |

ACL: `Users` = read-only. `SYSTEM`/`Administrators` = full control.

---

## Threading model

| Thread | Owner | Writes |
|--------|-------|--------|
| Service host (main) | `Worker.ExecuteAsync` | `_audio.Start/Stop`, event subscription |
| Audio capture thread | NAudio / `IWaveIn` impl | `_inCooldown` (via `StartCooldown`); reads `_running`, `_settings` |
| Pipe server loop | `Task.Run` inside `Worker` | `_paused`, `_cachedPinHmacKey`, `_cachedEffectiveSalt`, `_cachedCredentials` |
| UI thread (TrayApp) | WinForms message loop | `_paused` in `TrayApplicationContext` |
| Overlay pipe listener | `Task.Run` inside `PipeListener` | Invokes `_onBlackout` callback |

`AudioMonitor._running`, `_inCooldown`, `_settings` are all `volatile`.
`PipeServer` rate-limit state is protected by `_rateLimitLock`.
`PinRateLimiter` is fully synchronized under `_lock`.

---

## Security model

| Credential | Storage | Algorithm | Used for |
|-----------|---------|-----------|---------|
| PIN hash | `HKLM PinHash` | BCrypt work-factor 12 | UI-side verify (tray, settings, uninstall) |
| HMAC key | `HKLM PinHmacKey` | PBKDF2-SHA256 100K iters, 32 B | Pipe challenge-response; service never sees raw PIN |
| HMAC salt | `HKLM PinHmacSalt` | 16 random bytes | Sent in challenge; not secret |
| Recovery password | Compiled-in (XOR) | — | Fallback when PIN forgotten; uses fixed RecoverySalt |

Pipe ACLs:
- `screamsaver-control` (service server): LocalSystem + Admins + Interactive
- `screamsaver-overlay` (tray server): current user + Admins + LocalSystem

---

## Common starting points by task

| Task | Start here |
|------|-----------|
| Change threshold/cooldown/fade logic | `AudioMonitor.cs`, `AppSettings.cs`, `AppSettings.Validate()` |
| Change pipe command or add new command | `PipeMessages.cs`, `PipeServer.ExecuteCommand`, `DefaultPipeClient.SendControlAsync`, `TrayApplicationContext` |
| Change PIN/credential flow | `PinValidator.cs`, `PinCredentials.cs`, `HmacAuth.cs`, `SettingsForm.OnChangePin`, `PipeServer.UpdateSettings` |
| Add a settings field | `AppSettings.cs`, `SettingsRepository.Save/Load`, `SettingsForm.BuildSettings`, `AppSettings.Validate()` |
| Change overlay appearance | `OverlayForm.cs`, `OverlayManager.cs`, `AppSettings` fields |
| Change installer/uninstall | `Package.wxs`, `UninstallHelper/Program.cs`, `UninstallLogic.cs` |
| Add a test for AudioMonitor | `AudioMonitor.OnDataAvailable` (internal), `Func<IWaveIn>` ctor param — no real device needed |
| Debug a pipe auth failure | `PipeServer.ProcessMessage` → `VerifyHmac` → `HmacAuth.BuildMessage`; check `_cachedPinHmacKey` via `RefreshKeyCache` |
| Debug tray-app not launching | `TrayWatchdog.EnsureTrayRunning`, `IsCorrectInstance`, `LaunchInUserSession` |
