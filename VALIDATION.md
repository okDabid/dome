# Validation

## Executed in the Linux cloud build environment

- .NET 8 Release compilation and self-contained Windows x64 single-file publication.
- 10 core tests passed, 0 failed, 0 skipped: LRC offsets, repeated timestamps, fractional timestamps, metadata handling, blank instrumental sections, backward seeking and timeline boundaries; focus pause/resume, completion across machine sleep, single completion notification, repeated start, reset and invalid presets.
- The published file has a Windows PE x64 executable header; the ZIP and SHA-256 checksums are generated from that actual build.

These checks do **not** execute WPF, Core Audio, Windows media sessions, WMI brightness, tray integration, autostart, or Windows theme changes. No Windows desktop is attached to this environment. Windows runtime behavior and actual memory use remain unverified until the following checks are completed on Windows.

## Windows acceptance checklist

1. Extract the ZIP to a user-writable permanent folder; run Dome.exe on Windows 10 2004+ / Windows 11 x64 without administrator rights. Confirm one top-centered island and one tray icon. Launch a second time; confirm no duplicate island. Quit and relaunch.
2. Expand by click, hover, and Ctrl+Alt+Space. Collapse with Escape; disable hover and repeat. Test a shortcut conflict with another app. Hide/show from the tray. Confirm idle hover does not steal keyboard focus from the current app.
3. Change Windows app theme with System selected; confirm text, cards and controls update. Verify Light and Dark overrides remain fixed. Restart and confirm saved preferences.
4. Move between displays with different scaling (100%, 150%, 200%), reconnect an external display, and test a top/side taskbar. Confirm island stays inside the selected work area, and expanded content scrolls on a short display.
5. Play a known Spotify track and a YouTube music video in a browser with media integration enabled. Confirm metadata, artwork when available, play/pause, next/previous when permitted, seek and elapsed time. Switch active players, stop playback and close the player; confirm unavailable controls disable. Test a live stream and missing artwork.
6. Enable lyrics explicitly. Check a recording present in LRCLIB; verify line timing follows pause, resume, and backward/forward seek. Click a highlighted line if seeking is supported. Test absent artist, instrumental, unsynced-only lyrics, no match and offline mode. Disable lyrics and confirm new tracks cause no lyric HTTP requests.
7. Adjust volume and mute in Dome; verify actual Windows endpoint changes. Use hardware volume keys and Windows mixer; confirm compact readout follows them. Switch default output between speakers and headphones; repeat. Disconnect all audio outputs; verify a clear unavailable state and recovery when reconnected.
8. On a laptop supporting WmiMonitorBrightness, adjust brightness and verify the physical display changes without UI hangs. On an external monitor with DDC/CI enabled, confirm the selected display's hardware brightness changes. Verify the source label (DDC/CI selected display or WMI built-in display), switch displays and unplug/reconnect. On unsupported hardware, confirm unavailable state and working Windows display-settings shortcut.
9. Start/pause/resume/reset focus; check progress and compact countdown. Let it complete, including sleeping across the deadline. Confirm a single tray notification (Windows notification settings can suppress it). Quit/relaunch; focus should reset.
10. Add water, undo, restart, and verify persistence. Enable reminders and verify an hourly notification within daytime hours while running; cross midnight to verify the daily log resets.
11. Add and finish tasks; verify persistence. Add files by drop and picker; open, remove and move originals. Confirm only shortcuts are removed, unavailable files produce an explanation, and the list stops at 30.
12. Enable startup; verify the HKCU Run entry points to the quoted executable path. Sign out/in; verify startup. Disable it, quit and relaunch; verify it stays disabled. No admin prompt should occur.
13. Measure Task Manager working set, private working set and CPU after 5 minutes collapsed/idle and 5 minutes playing with synced lyrics. Record Windows version, hardware, scaling, and .NET version. Check for stable memory through track changes; avoid interpreting the first-launch JIT peak as idle memory. No RAM budget has yet been measured.

Portable builds are unsigned. Signing/notarization is a separate distribution step and requires the owner's certificate; no certificate or signature is fabricated.
