# ysfUtil

Small helpers for Windows 11, on a hotkey. Lives in the notification area – no window, no installer.

## Features

- **Toggle taskbar** (`Ctrl + Alt + D`): switches between auto-hide and always shown.
- **Toggle desktop icons** (`Ctrl + Alt + T`): hides or shows all desktop icons.
- **Audio output** (`Ctrl + Alt + A`): cycles through selected playback devices. Pick an
  output directly in the flyout, or choose the devices to cycle through in Settings.
  Communication audio and the short switch banner can be enabled independently.
  The translucent banner appears at the bottom right, never takes focus and lets clicks pass
  through to the app beneath it. Audio errors use the same overlay, not Windows notifications.
  Default-device confirmation waits asynchronously for Windows to finish applying a change.
  Connected devices are detected automatically; disconnected selections are remembered and
  skipped until they reconnect. Devices added after configuring a cycle are opt-in.
  **Import SoundSwitch devices** reads your existing playback selection without changing
  SoundSwitch. The hotkey remains configurable in ysfUtil; no microphone settings are imported.
- **Shutdown timer**: choose 15, 30 or 60 minutes, or enter 1–1440 minutes. Shows a live
  countdown and warns during the final minute. Cancel in the flyout.
  Keep ysfUtil running: quitting cancels the timer. Programs with unsaved work can block shutdown.
- **Volume controls**: system-wide sliders for the Windows default output and default microphone,
  with their device names and current percentages. Changes made in other apps are reflected live.
  Set a microphone level and enable **Keep microphone at …%** to restore it when another app
  lowers or raises it. The lock follows the default microphone, preserves mute, and stays active
  in the tray. It is off by default; the chosen target and enabled lock are remembered.
  Native volume-change notifications restore the level promptly, with a 250 ms fallback check
  while locked. ysfUtil must remain running. Disabling Volume controls stops the lock.

Left- or right-click the tray icon to open a flyout where you can turn features on or off,
change their hotkeys and manage the shutdown timer.
Enable or disable features in Settings. Disabled features are hidden and their hotkeys are
unregistered; disabling the shutdown timer also cancels a running countdown.
On opening, the main view determines the flyout height. Settings uses the same dimensions;
switching views never resizes or repositions the window. Changes to enabled features affect
the fitted height on the next opening. Opening, closing and view changes fade gently,
respecting the Windows animation setting.
The minimize button hides it in the tray while keeping ysfUtil and any active timer running.
Click a feature heading to fold its options away. The fold state is remembered across restarts;
hotkeys, microphone locking and timers keep working. A running shutdown timer can still be
cancelled while folded. The open flyout keeps its position and size during folding.
Mouse-wheel scrolling eases smoothly, including quick repeated turns and direction changes.
Dragging the scrollbar and keyboard navigation remain immediate. Motion follows the Windows
animation preference. Manual startup shows a brief, click-through overlay at the bottom right,
above the taskbar. Audio messages use the same position; autostart stays silent.

Audio switching changes Windows' default playback output. Apps explicitly assigned to a
particular device can keep using that device; microphone switching, profiles and per-app
routing are not part of this feature. Audio Mirror continues to handle simultaneous playback
on multiple outputs independently.

## Updates

Settings → Updates offers **Check for updates** and a one-click **Update to …** button.
The default **Offer updates** checks stable GitHub releases on startup and once per day
while running; a small dot on the settings icon signals an available update. No dialog
or Windows notification appears. Choose **Install automatically** for background updates,
or **Never check automatically** to disable background requests (manual checks still work).

Updates download the release's `ysfUtil.exe`, verify its size and GitHub SHA-256 digest,
then use a temporary copy of the app to wait for exit, atomically replace the portable EXE,
and restart it in its previous visible/tray state. Settings and autostart are preserved;
the previous EXE is retained alongside it as `ysfUtil.exe.previous`. A running shutdown
timer blocks installation until cancelled. Download failures leave the running app intact.
The destination folder must be writable, and updating requires the published single-file EXE.

Publishing: set `<Version>` in `ysfUtil.csproj` and push the matching `vX.Y.Z` tag.
`.github/workflows/release.yml` tests, publishes and attaches the portable EXE to a stable
GitHub release. Keep the asset name **ysfUtil.exe**: installed updaters depend on it.
Source-only releases or assets without a GitHub SHA-256 digest offer the release page instead.

## Requirements

[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

## Build

```
build.cmd
```

Output: `dist\ysfUtil.exe`.

Timer and audio logic checks (with a simulated clock, shutdown action and audio devices):

```
dotnet run --project Tests/ShutdownTimer.Tests.csproj
```

## License

MIT

Audio enumeration uses NAudio.Wasapi / NAudio.Core 2.2.1 (MIT), also used by Audio Mirror.
See THIRD-PARTY-NOTICES.txt for attribution.
