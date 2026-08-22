<div align="center">
  <img src="src/TetherMate/ico/TetherMateLogo.png" alt="TetherMate logo" width="92">
  <h1>TetherMate</h1>
  <p><strong>Use Virtual Desktop on Meta Quest through a USB cable.</strong></p>
  <p>
    <a href="https://github.com/Swmarly/TetherMate/releases/latest">Download for Windows</a>
    ·
    <a href="https://github.com/Swmarly/TetherMate/actions/workflows/build.yml">Build status</a>
    ·
    <a href="https://github.com/Swmarly/TetherMate/issues/new">Report a problem</a>
  </p>
</div>

TetherMate is a Windows 10/11 app that gives an Android-based VR headset a network path through its USB data cable. It is aimed at Meta Quest and Virtual Desktop, and uses `adb` plus [gnirehtet](https://github.com/Genymobile/gnirehtet) without requiring root, a special Windows network driver, or administrator access.

TetherMate provides the wired network path. You still need Virtual Desktop on the headset and Virtual Desktop Streamer on the PC. It is independent of Meta Quest Link and does not replace Virtual Desktop.

## Why this rebuild works

The former implementation launched gnirehtet with invalid `-s <serial>` syntax and did not tell gnirehtet where the bundled `adb.exe` was located. It then displayed **Running** before checking whether a tunnel or headset client existed.

The rebuilt app uses a verified startup sequence:

1. Start the PC relay and confirm that it stays alive.
2. Invoke `gnirehtet start <serial>` with the correct positional serial.
3. Pass absolute `ADB` and `GNIREHTET_APK` paths to gnirehtet.
4. Verify the `adb reverse` mapping on TCP 31416.
5. Wait for the headset VPN client to actually connect before showing **Connected**.

It also reconnects after cable interruptions, backs off after failures, preserves manual disconnects, keeps useful local logs, and never kills another program’s ADB server or unrelated gnirehtet process.

## Setup

You need:

- Windows 10 or 11, x64
- Meta Quest 2, Quest 3, Quest 3S, or another compatible Android headset
- Developer Mode and USB debugging enabled on the headset
- A USB data cable; USB 3 is strongly recommended for VR streaming
- Virtual Desktop on the headset and Virtual Desktop Streamer on the PC

### First connection

1. Download the latest `TetherMate-…-win-x64.zip` from [Releases](https://github.com/Swmarly/TetherMate/releases/latest).
2. Extract the ZIP and run `TetherMate.exe`.
3. Connect and wake the headset.
4. Put on the headset and accept **Allow USB debugging**. Choose **Always allow from this computer** when available.
5. Accept Android’s VPN connection prompt when TetherMate starts the link.
6. Wait for TetherMate to show **Connected over USB**, then open Virtual Desktop.

To prove that Virtual Desktop is using the cable, temporarily turn off Wi‑Fi on the headset after TetherMate turns green. The PC must retain its normal network connection.

## What the status means

| Status | Meaning | Action |
|---|---|---|
| Connect your headset | ADB sees no device | Check the cable, USB port, headset power, and Developer Mode |
| Allow USB debugging | The device is present but unauthorized | Accept the prompt inside the headset |
| Finish setup in the headset | The PC relay and USB tunnel are ready | Accept the VPN prompt inside the headset |
| Connected over USB | The headset client reached the PC relay | Open Virtual Desktop |
| Needs attention | A command, relay, or tunnel failed | Run **Diagnostics**, then **Restart link** |

TetherMate continues running in the notification area by default. Right-click its tray icon to connect, disconnect, diagnose, or exit.

If the bundled headset helper is missing, damaged, or conflicts with an older installation, open **Diagnostics** and select **Repair headset client**. TetherMate will uninstall it, install the bundled copy, and rebuild the link.

## Troubleshooting

### No headset appears

- Confirm that the cable supports data; some USB-C cables only charge.
- Connect directly to a USB 3 port instead of a hub.
- Disable and re-enable Developer Mode if Meta’s USB debugging prompt has disappeared.
- Wake and unlock the headset before reconnecting the cable.

### It waits for the headset VPN

Put on the headset and look for Android’s VPN permission dialog. If no dialog appears, select **Restart link**. TetherMate only reports Connected after gnirehtet logs a real client connection.

### Virtual Desktop cannot find the PC

- Start Virtual Desktop Streamer on Windows first.
- Restart Virtual Desktop on the headset after the wired link turns green.
- Confirm that the PC itself still has internet/network access.

This is an IPv4 VPN-over-ADB path, not a true Ethernet bridge. Virtual Desktop may describe the PC as being on a different network or enforce its own bitrate behavior; TetherMate cannot override Virtual Desktop’s limits.

### Windows SmartScreen appears

The executable is built publicly by GitHub Actions but is not currently code-signed with a commercial certificate. Windows may therefore show an unknown-publisher warning. Every release includes `SHA256SUMS.txt` so the downloaded ZIP can be verified.

## Privacy

TetherMate has no telemetry and uploads nothing. Local lifecycle and command logs are stored under:

```text
%LOCALAPPDATA%\TetherMate\logs
```

Logs rotate automatically and may include the connected device serial. They leave the PC only if you copy or share them yourself.

## Build from source

Requirements:

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- PowerShell 5.1 or newer

Run either command from the repository root:

```powershell
./build.ps1
```

```bat
build.bat
```

The script restores dependencies, builds with warnings treated as errors, runs the dependency-free core test suite, creates a compressed self-contained EXE, packages the license files, and writes:

```text
dist/TetherMate.exe
dist/TetherMate-1.0.0-win-x64.zip
dist/SHA256SUMS.txt
```

## Publish a GitHub release

There are two supported paths:

1. In GitHub, open **Actions → Release → Run workflow**, enter a version such as `1.0.0`, and run it.
2. From a clean local checkout, run:

   ```powershell
   ./release.ps1 -Version 1.0.0
   ```

The local script builds and tests first, then pushes `v1.0.0`. The release workflow builds on a clean `windows-latest` runner and publishes the EXE, ZIP, checksums, and generated release notes.

## Architecture

- `TetherMate.Core` contains the ADB parser, safe process runner, verified gnirehtet session, and relay-state detection.
- `TetherMate` is the WPF desktop/tray app and uses .NET 10’s Fluent theme foundation.
- `TetherMate.Core.Tests` is a package-free executable test harness, so contributors do not depend on a third-party test framework.
- Bundled runtime files are SHA-256 checked and atomically refreshed under `%LOCALAPPDATA%\TetherMate\runtime`.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the connection lifecycle and design decisions.

## License and third-party software

TetherMate is licensed under the [Apache License 2.0](LICENSE).

- gnirehtet is Apache-2.0 licensed; see [`licenses/gnirehtet-LICENSE.txt`](licenses/gnirehtet-LICENSE.txt).
- Android platform-tools notices are distributed in [`NOTICE.txt`](NOTICE.txt).
- The release ZIP contains all applicable license and notice files, and the app extracts a local copy accessible from **Third-party notices**.
