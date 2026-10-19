# ysfUtil

Small helpers for Windows 11, on a hotkey. Lives in the notification area – no window, no installer.

## Features

- **Toggle taskbar** (`Ctrl + Alt + D`): switches between auto-hide and always shown.
- **Toggle desktop icons** (`Ctrl + Alt + T`): hides or shows all desktop icons.
- **Shutdown timer**: choose 15, 30 or 60 minutes, or enter 1–1440 minutes. Shows a live
  countdown and warns during the final minute. Cancel in the flyout.
  Keep ysfUtil running: quitting cancels the timer. Programs with unsaved work can block shutdown.

Left- or right-click the tray icon to open a flyout where you can turn features on or off,
change their hotkeys and manage the shutdown timer.
Enable or disable features in Settings. Disabled features are hidden and their hotkeys are
unregistered; disabling the shutdown timer also cancels a running countdown.
On opening, the main view determines the flyout height. Settings uses the same dimensions;
switching views never resizes or repositions the window. Changes to enabled features affect
the fitted height on the next opening. Opening, closing and view changes fade gently,
respecting the Windows animation setting.
The minimize button hides it in the tray while keeping ysfUtil and any active timer running.

## Requirements

[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

## Build

```
build.cmd
```

Output: `dist\ysfUtil.exe`.

Timer logic checks (with a simulated clock and shutdown action):

```
dotnet run --project Tests/ShutdownTimer.Tests.csproj
```

## License

MIT
