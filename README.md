# Dome

A native Windows desktop island for music, focus, and everyday controls. A quiet pill at the top of your display expands into a soft, rounded dashboard with lime accents, Fluent icons, and automatic light/dark appearance. No Electron, browser runtime, account, or administrator privileges.

## Run the executable

Download/extract the Windows x64 build and run **Dome.exe** on Windows 10 2004 (build 19041) or newer, including Windows 11. The portable, self-contained executable includes .NET; no runtime installation is needed. Keep it in a permanent location before enabling **Start when I sign in**. Builds are unsigned; verify the supplied SHA-256 and use your normal Windows security policy.

- Click the pill or press **Ctrl+Alt+Space** to expand/collapse. Hover expansion is optional.
- Press **Escape** to collapse. Right-click the island for the tray menu.
- Double-click the tray icon to open it. Use the tray menu to move displays, hide/show, or quit.
- Drop files on the island to create shelf shortcuts. Removing a shortcut never deletes the file.
- Preferences, tasks, and daily hydration are stored in `%LOCALAPPDATA%\Dome\settings.json`. No telemetry or credentials are collected. Delete that folder to reset preferences after quitting.

## What works, and what depends on Windows

| Feature | Implementation / limits |
| --- | --- |
| Music metadata, artwork, play/pause, previous/next, seek | Windows Global System Media Transport Controls. Uses the current session selected by Windows. Spotify and browsers playing YouTube can expose sessions; any other app must implement Windows media integration. The player decides which controls are available. Protected artwork, live streams, ads and missing metadata are handled as unavailable, rather than simulated. |
| Lyrics | Explicitly opt in under Music or Settings. HTTPS requests to `lrclib.net` include title, artist, album and recording duration. Timed LRC lines follow playback and seeking; click the highlighted line to seek to its start if the player permits it. Unsynced lyrics are shown as text. Missing recordings, missing metadata, instrumentals and network errors have fallback states. No promise of lyrics for every platform or track. Only the latest 12 successful lookups are cached, in memory. |
| System volume / mute | Native Core Audio, including notifications for external changes and default output changes. A compact volume readout appears when the default endpoint volume changes. Works on the default multimedia endpoint. |
| Brightness | Native DDC/CI hardware control for a compatible monitor on the selected display; Windows WMI fallback for supported built-in screens. The label identifies which route is active. Enable DDC/CI in an external monitor's menu if needed. Unsupported monitors/drivers show an unavailable state and Windows display-settings shortcut. No simulated dark overlay. |
| Focus | 15/25/50-minute presets, pause/resume/reset, progress, tray notification. Deadline-based timing survives Windows sleep while the process stays running. Timer state is intentionally not persisted after quitting. |
| Tasks | A small persistent checklist, capped at 30 tasks. Checking a task completes/removes it. |
| Water | Persistent daily 250/500 ml drink log, undo, 2000 ml visual target. Optional hourly reminders between 08:00 and 22:00 local time while the app runs. Not medical advice. |
| Shelf | Up to 30 persistent local file/folder shortcuts, choose files or drag/drop. User click opens the original through the Windows shell. Moving/deleting the original makes the shortcut unavailable. |
| Desktop integration | Battery/charging status, per-user startup toggle, single-instance guard, tray menu, global shortcut, display switching and DPI-aware positioning. Shortcut conflicts are reported. |
| Appearance | System/light/dark choice; follows Windows preference changes when System is selected. System Fluent/MDL2 icon font, keyboard access and named controls. |

The design takes inspiration from the feature description and visual vocabulary of [Nusa](https://rama2555.github.io/nusa/). Its public website source was consulted; no Nusa application source or artwork is included. The [Nicheloom reference](https://www.nicheloom.com/launches/55425/) could not be inspected because the development environment denied that domain, so this is an original implementation, not a verified reproduction of that product.

## Build

Use .NET 8 SDK **8.0.408 or a newer 8.0 patch** (`global.json`). The UI uses C#/WPF for native Windows interoperability and avoids a bundled web engine. Windows APIs require Windows at runtime, but the executable can be cross-published from Linux.

On Windows, from the repository root:

```powershell
.\scripts\build.ps1
```

On Linux with .NET 8 on `PATH`:

```sh
bash scripts/build.sh
```

Both scripts run the core test suite, publish a self-contained x64 single-file executable to `artifacts/win-x64/Dome.exe`, package a portable ZIP, and generate SHA-256 checksums. Dependency restore requires NuGet (`api.nuget.org` and its package CDN); lyric lookup requires `lrclib.net` on the Windows user's network. There are no build or application API keys.

For development on Windows:

```powershell
dotnet run --project src/Dome/Dome.csproj
```

Do not create another checkout/worktree for setup: use this isolated task's existing checkout. The Linux cloud environment can build and test core logic, but cannot launch WPF or verify Windows hardware behavior. See [VALIDATION.md](VALIDATION.md) for the explicit validation boundary and a Windows smoke-test checklist. `.github/workflows/build.yml` builds and uploads artifacts on Windows CI when pushed; it does not publish a release or sign binaries.

## Resource use

WPF with workstation GC, event-driven media/audio/theme updates, small decoded artwork, bounded lyric cache, and no background web view. The UI ticker slows to once per 30 seconds when collapsed with no active playback or focus timer; it runs once per second during playback, focus, or expansion. Driver discovery and network calls run off the UI thread. No continuous animations or forced working-set trimming are used. The single-file runtime makes the download larger than the source app; disk size is not RAM usage. Actual idle/playing RAM and CPU must be measured on Windows; no unmeasured RAM target is claimed.

## Uninstall

Turn off **Start when I sign in**, quit from the tray, then delete the executable and optionally `%LOCALAPPDATA%\Dome`. Startup registration is `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Dome`.
