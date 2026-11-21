# ysfUtil

Small helpers for Windows 11, on a hotkey. Lives in the notification area.

## Download

**[Download Setup (recommended)](https://github.com/yyusvf/ysfUtil/releases/latest/download/ysfUtil-Setup.exe)**

Small installer with a Start menu entry and an uninstaller. It reuses an installed .NET 8
Desktop Runtime (x64); if missing, it downloads the official Microsoft installer, checks its
SHA-256 hash and installs the runtime before continuing. Windows may ask for administrator
permission for the runtime installation. The app itself installs for your Windows user.
Settings survive updates and reinstalls.

**[Portable EXE (alternative)](https://github.com/yyusvf/ysfUtil/releases/latest/download/ysfUtil.exe)**

Run without installation. Requires the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).
Close an older running copy before installing or launching another copy. Existing settings
are shared, so you can move from portable to Setup without reconfiguring the app.

## Features

- **Toggle taskbar** (`Ctrl + Alt + D`): switches between auto-hide and always shown.
- **Toggle desktop icons** (`Ctrl + Alt + T`): hides or shows all desktop icons.
- **Toggle display** (`Ctrl + Alt + P`): switches between "PC screen only" and "Second screen
  only", like the Win + P panel. From Extend or Duplicate, the first press goes to PC screen only.
- **Audio output** (`Ctrl + Alt + A`): cycles through selected playback devices. Pick an
  output directly in the flyout, or choose the devices to cycle through in Settings.
  Communication audio and the short switch banner can be enabled independently.
  The translucent banner appears in your selected corner (bottom right by default), never takes focus and lets clicks pass
  through to the app beneath it. Audio errors use the same overlay, not Windows notifications.
  Default-device confirmation waits asynchronously for Windows to finish applying a change.
  Connected devices are detected automatically; disconnected selections are remembered and
  skipped until they reconnect. Devices added after configuring a cycle are opt-in.
  The list shows the outputs Windows knows right now. When Windows re-creates an output (new
  driver, another port, a "2- " in its name), it keeps its place in the cycle instead of
  appearing twice.
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

All of the above make up the built-in **Tools** module.

### Plugins

Settings → Modules works like a small plugin browser: every module has a switch, and plugins
that aren't installed yet have an **Install** button. A disabled module is never loaded and its
hotkeys are unregistered. Plugins live in their apps' own repositories and come from their
GitHub releases (`<id>.ysfplugin.zip`, checked against GitHub's SHA-256 like ysfUtil's own
updates), so releasing the app also updates the plugin. Plugin updates follow Settings →
Updates: offered with an **Update** button, installed automatically, or never checked. A new
version of a plugin that is already loaded takes effect after **Restart**.

- **Audio Mirror** ([yyusvf/audio-mirror](https://github.com/yyusvf/audio-mirror)): plays
  Windows audio on several output devices at once. Switch devices on in the flyout and set each
  one's volume. Its settings pick the source device, the buffer, and which apps are mirrored to
  each device. An optional hotkey turns all mirroring off and restores it on the next press. On
  first start it takes over the standalone Audio Mirror's devices, volumes, app mix and hotkey;
  quit the standalone app so the sound isn't mirrored twice.
- **DS5 BT Haptics** ([yyusvf/ds5-bt-haptics](https://github.com/yyusvf/ds5-bt-haptics)):
  DualSense HD haptics, adaptive triggers and light bar over Bluetooth, by presenting a virtual
  wired DualSense via usbip-win2. The flyout card walks through the driver setup one step at a
  time, shows controller and haptics status, and has a test button. The bridge runs in its own
  ysfUtil process, so a problem there never takes the tray down. Its settings hold strength,
  latency, the games list, the log, and **Make controller visible to games** to undo the HidHide
  setup.

Audio Mirror and DS5 BT Haptics were built into ysfUtil 0.3. If one was switched on, ysfUtil
installs its plugin on the next start and keeps its settings.

Plugins are stored in `%LOCALAPPDATA%\ysfUtil\plugins\<id>\<version>`, their settings in
`%APPDATA%\ysfUtil\plugins\<id>.json`. Each loads into its own assembly context; libraries
ysfUtil already ships (NAudio) are shared. A plugin's own process (`ysfUtil.exe
--plugin-process <id> <version> ...`) runs its `IPluginProcess`.

**Writing a plugin:** reference `Sdk/YsfUtil.Sdk.csproj` with `Private="false"`, implement
`IModule` (constructor taking `IModuleContext`), file cards under a `ModuleCategory`, and ship a
`plugin.json` next to the assembly:

```json
{ "id": "my-plugin", "name": "My Plugin", "description": "…", "glyph": "\uE700",
  "version": "1.0.0", "apiVersion": 1, "assembly": "My.Plugin.dll", "module": "My.Plugin.Module" }
```

Cards use ysfUtil's styles by key (`SettingsCard`, `FeatureExpander`, `BodyText`, `SecondaryText`,
`StandardButton`, `GlyphButton`, `ToggleSwitch`, `VolumeSlider`, …) and `HotkeyBox`/`SectionState`
from the SDK. Try a local build with `ysfUtil.exe --install-plugin <zip>`; set `YSFUTIL_PLUGINS`
to a folder to keep test installs apart. The app repositories above show the full setup,
including the release workflow.

Left- or right-click the tray icon to open a flyout where you can turn features on or off,
change their hotkeys and manage the shutdown timer.
The flyout groups cards by category rather than by module: **Audio** (output, volume and
microphone, Audio Mirror), **Desktop** (taskbar, desktop icons, display), **System** (shutdown timer) and
**Controller** (DS5). A heading hides when nothing below it is enabled. Settings starts with
Modules and the Tools switches, followed by the same categories and the app's own settings.
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
animation preference. Manual startup shows a brief, click-through overlay. Settings →
Notification position lets you choose any of the four screen corners and preview the result.
Audio messages use the same saved position; autostart stays silent.
Feature headers highlight across their full width. Audio polling avoids redundant UI updates
and unnecessary background reads; microphone locking continues while the app is in the tray.

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

Installed versions download `ysfUtil-Setup.exe`, verify its size and GitHub SHA-256 digest,
then wait for ysfUtil to exit before running the setup silently in the existing installation
directory. Portable versions instead download `ysfUtil.exe` and atomically replace the EXE,
keeping the previous file as `ysfUtil.exe.previous`. A temporary helper restarts the app in
its previous visible/tray state. Settings and the user's autostart choice are preserved.
A running shutdown timer blocks installation until cancelled. Download failures leave the
running app intact. The destination folder must be writable. Installed copies never fall
back to replacing themselves with a portable build when a release has no setup.
The runtime check also runs during silent setup updates; download or installation failures
stop Setup before replacing the application. Existing runtime installations are reused offline.

Publishing: set `<Version>` in `ysfUtil.csproj` and push the matching `vX.Y.Z` tag.
`.github/workflows/release.yml` tests, builds the setup and portable EXE, and presents Setup
as the recommended download in a stable GitHub release. Keep the asset names
**ysfUtil-Setup.exe** and **ysfUtil.exe**: existing updaters depend on them.
Source-only releases or assets without a GitHub SHA-256 digest offer the release page instead.

## Requirements

Windows x64 and [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).
Setup downloads it only if missing; portable users install it separately. A missing runtime
requires an internet connection and permission to install Microsoft's system-wide runtime.

## Build

```
build.cmd
```

Output: `dist\ysfUtil.exe`.

To build both downloads, install [Inno Setup 6](https://jrsoftware.org/isinfo.php) and run:

```powershell
./build-release.ps1
```

Output: `dist\packages\ysfUtil-Setup.exe` (recommended), `dist\packages\ysfUtil.exe` (portable).
The runtime is not bundled. Its versioned Microsoft URL and SHA-256 are pinned in
`setup/DesktopRuntime.iss`; verify Microsoft's release-metadata SHA-512 and Authenticode
signature before updating that pin.

Timer and audio logic checks (with a simulated clock, shutdown action and audio devices):

```
dotnet run --project Tests/ShutdownTimer.Tests.csproj
```

## License

MIT

Audio enumeration uses NAudio.Wasapi / NAudio.Core 2.2.1 (MIT), also used by Audio Mirror.
See THIRD-PARTY-NOTICES.txt for attribution.
