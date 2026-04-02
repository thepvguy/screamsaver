namespace Screamsaver.TrayApp;

internal static class HelpText
{
    public const string Overview = """
# What is Screamsaver?

Screamsaver is a parental control tool for Windows. It listens to the microphone in the background and, when a child gets too loud during gaming, it instantly covers every monitor with a dark overlay. The screen fades back to normal after a few seconds.

The idea is simple: loud = screens go dark. Quiet down = screens come back.

## How it works

1. A Windows Service runs silently in the background, always listening to the microphone.
2. When the volume level crosses the threshold you set, the service sends a trigger to the tray app.
3. The tray app covers all connected monitors with a configurable overlay.
4. After the fade-out duration elapses, the overlay disappears on its own.
5. A cooldown period prevents the overlay from retriggering immediately if the noise continues.

## First-time setup

After installing, right-click the tray icon and choose Settings. You will be asked for the recovery password (printed during installation). Use it to set a personal PIN and configure the sensitivity to match your child's environment.

## The service runs even when you are not logged in

Because Screamsaver runs as a Windows Service, it starts automatically with Windows and monitors the microphone for any user session on the computer. You do not need to launch anything manually.
""";

    public const string TrayIcon = """
# The System Tray Icon

Screamsaver lives in the system tray (bottom-right corner of the taskbar). Right-clicking it opens the menu.

## Settings

Opens the Settings window where you can tune sensitivity, the overlay appearance, and your PIN. A PIN is required to open this window.

## Pause / Resume

Temporarily stops microphone monitoring so the overlay will not trigger. Useful when you need the child to be loud for a moment (a birthday call, for example). A PIN is required. The tray icon tooltip changes to "Screamsaver (paused)" as a reminder.

To resume monitoring, open the same menu item again and enter your PIN.

## Exit

Closes the tray app. A PIN is required.

Note: closing the tray app does not stop the Windows Service. The service will relaunch the tray app within a few seconds. To fully stop Screamsaver, you must pause it through the Settings menu, or stop the service from Services Manager (requires admin rights).

## If the tray icon disappears

The Windows Service watches whether the tray app is running and restarts it automatically within 5 seconds. If the icon does not return, check that the service is running in Services (search "Services" in the Start menu, look for "Screamsaver Monitor Service").
""";

    public const string Settings = """
# Settings Reference

All settings require a PIN to save. Click Preview Overlay at any time to see how the current overlay settings will look without needing to trigger from audio.

## Volume threshold (dBFS)

Controls how loud the microphone input must be before the overlay triggers. The value is in decibels relative to full scale (dBFS), where 0 is the loudest possible signal.

• -20 dBFS (default) — triggers on moderately loud speech or a raised voice.
• -30 dBFS — more sensitive; triggers on normal conversation.
• -10 dBFS — less sensitive; only triggers on very loud shouting.

Start at the default and adjust based on how your child's environment sounds. Use the Preview Overlay button to test, and watch the threshold slider while the child plays to see where their normal voice sits.

## Cooldown (seconds)

After the overlay triggers, it will not trigger again for this many seconds. This prevents the screen from flickering on and off repeatedly if the child stays loud.

• Default: 30 seconds.
• Increase this if you want a longer enforced break.
• Minimum: 5 seconds.

## Fade-in duration (ms)

How long the overlay takes to appear after a trigger. 0 means the screen goes dark instantly.

• 0 ms (default) — instant blackout. Most effective as a consequence.
• 200-500 ms — a quick but visible fade in.

## Fade-out duration (ms)

How long the overlay takes to fade back to the normal screen after it has fully appeared.

• 5000 ms (default) — 5 seconds. Enough time to make an impression.
• Increase for a longer interruption; decrease for a shorter one.

## Max opacity (%)

How dark the overlay gets at its peak. 100% is fully opaque (completely black or the chosen color). Lower values let the screen content show through faintly.

• 100% (default) — screen is completely covered.
• 70-80% — screen is heavily dimmed but still somewhat visible.

## Overlay color

The solid color shown during the overlay. Default is black. Click Choose Color to pick any color — some parents use red or a bright warning color to make the trigger more noticeable.

## Overlay image

An optional image to display on top of the overlay color. Use this to show a message, a warning graphic, or anything else. The image is scaled to fit each monitor while preserving its aspect ratio.

Leave this blank to show only the solid color.

## Change PIN

Opens a dialog to change your PIN. You must enter the current PIN (or the recovery password) before setting a new one. Enter the new PIN twice to confirm.

Choose a PIN your child cannot easily guess, but that you will remember. Store it somewhere safe.
""";

    public const string PinAndSecurity = """
# PIN & Security

## What the PIN protects

Every action that could disable or weaken Screamsaver requires the correct PIN:

• Opening Settings
• Pausing monitoring
• Closing the tray app
• Changing any setting (including the PIN itself)
• Uninstalling the application

## Setting your PIN for the first time

After installation, the PIN field is blank. The first time you open Settings, use the recovery password printed during installation. Go to Change PIN, enter the recovery password as the current PIN, then set a personal PIN.

## If you forget your PIN

Enter the recovery password in any PIN prompt instead of your PIN. The recovery password always works as a fallback. It was shown once during installation — if you did not record it, refer to your installation documentation or contact whoever built the installer.

## How the PIN is stored

Your PIN is never stored in plain text. It is run through the BCrypt hashing algorithm and only the hash is saved in the Windows registry. Even if someone reads the registry directly, they cannot reverse the hash to find your PIN.

## Hardening against removal

Screamsaver is designed to resist a determined child:

• The Windows Service automatically restarts if stopped (3 attempts, then waits for a restart).
• The tray app relaunches within 5 seconds if closed or killed.
• The settings registry key is read-only for standard user accounts.
• The install folder is read-only for standard user accounts.
• The autostart registry entry is in the system-wide location and requires admin rights to remove.
• Uninstalling through Add or Remove Programs requires the correct PIN.

A child with a standard Windows account cannot permanently disable Screamsaver through normal means.
""";

    public const string Troubleshooting = """
# Troubleshooting

## The overlay never triggers

• Check that the microphone is working in Windows Sound settings (Settings > System > Sound).
• Lower the threshold (make it more negative, e.g. -30 dBFS). The default assumes a moderately loud voice close to the mic.
• Make sure monitoring is not paused — check the tray icon tooltip.
• Confirm the service is running: open Services (search "Services" in Start), look for "Screamsaver Monitor Service" with status Running.

## The overlay triggers too easily or too often

• Raise the threshold (make it less negative, e.g. -15 dBFS).
• Increase the cooldown so it cannot retrigger as frequently.
• Check whether background noise (fans, music, TV) is being picked up. Try setting the microphone as the Communications device in Windows Sound settings and lower its input level.

## The overlay disappears too quickly

Increase the Fade-out duration in Settings. Values between 5000 and 15000 ms (5-15 seconds) work well for most situations.

## The tray icon is missing

The service watchdog will relaunch the tray app automatically. Wait 10 seconds and check again. If it does not return, open Services and verify "Screamsaver Monitor Service" is running.

## I forgot my PIN

Enter the recovery password in any PIN prompt. It was displayed once during installation. If it has been lost, the application will need to be reinstalled.

## Settings changes do not seem to take effect

The service re-reads settings as soon as they are saved through the Settings window. If the service is not running, changes are written to the registry but will not take effect until the service starts. Confirm the service is running in Services.

## I need to temporarily disable Screamsaver

Use Pause / Resume from the tray menu (requires PIN). This stops monitoring without closing the app. Resume it the same way when done.
""";
}
