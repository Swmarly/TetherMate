
<a id="readme-top"></a>
<br />
<div align="center">
  <a href="https://github.com/Swmarly/TetherMate">
    <img src="src/TetherMate/ico/TetherMateLogo.png" alt="Logo" width="80" height="80">
  </a>

  <h3 align="center">TetherMate</h3>

  <p align="center">
    Automatically manages gnirehtet reverse tethering for Quest and other Android VR headsets over USB.
    <br />
    <a href="https://github.com/Swmarly/TetherMate"><strong>Explore the docs »</strong></a>
    <br />
    <br />
    <a href="https://github.com/Swmarly/TetherMate">View Demo</a>
    &middot;
    <a href="https://github.com/Swmarly/TetherMate/issues/new?labels=bug&template=bug-report---.md">Report Bug</a>
    &middot;
    <a href="https://github.com/Swmarly/TetherMate/issues/new?labels=enhancement&template=feature-request---.md">Request Feature</a>
  </p>
</div>



# TetherMate

TetherMate is a Windows 10/11 desktop app that automatically manages `adb` + `gnirehtet` reverse tethering for compatible Android-based VR headsets (Meta Quest 2 and similar). It lets you use a **wired** network connection over USB while still supporting Wi‑Fi–based streaming apps like **Virtual Desktop**.

## Table of contents

- [What it does](#what-it-does)
- [How it works](#how-it-works)
- [Requirements](#requirements)
- [Usage](#usage)
  - [Select a target device](#select-a-target-device)
  - [Enable ADB debugging](#enable-adb-debugging)
  - [Troubleshooting](#troubleshooting)
- [Build & package (single-file EXE)](#build--package-single-file-exe)
- [Compatibility notes](#compatibility-notes)
- [Built with](#built-with)

## What it does

TetherMate provides a stable, low-latency wired alternative for VR streaming by managing reverse tethering over USB. It watches connected ADB devices, selects a target device, and keeps `gnirehtet` running only when the device is ready.

## How it works

- The app bundles the `adb` and `gnirehtet` binaries already in this repository.
- On startup it extracts the binaries into `%LOCALAPPDATA%\TetherMate\bin`.
- A background monitor refreshes the device list via `adb devices -l` and probes ready devices using `adb shell getprop`.
- A device is considered **ready** when:
  - ADB reports it in `device` state, **and**
  - It responds to `adb shell getprop` probes.
- `gnirehtet` auto-starts when the selected device becomes ready and stays ready for a few seconds (debounce).
- `gnirehtet` auto-stops when the selected device is disconnected, unauthorized, offline, or otherwise not ready.
- If you change the selected target device, the app stops the current session and starts a new one if the new device is ready.

## Requirements

- Windows 10/11
- .NET 8 SDK (build-time only)

## Usage

### Select a target device

1. Connect your headset/device via USB and accept the ADB authorization prompt inside the headset.
2. The device will appear in the **Connected ADB devices** list.
3. Use the **Target device** dropdown to select the device to manage.
4. Manual **Start / Stop / Restart** controls are provided for overrides.

### Enable ADB debugging

The headset must have **Developer Mode** enabled and **USB/ADB debugging** turned on, or the app will never see it as ready.

#### Meta Quest 2/3 (and similar)

1. Enable **Developer Mode** for the headset in the Meta Quest mobile app (Device settings → Developer Mode).
2. On the headset, open **Settings → System → Developer** and toggle **USB debugging** on.
3. Connect the headset via USB and accept the **Allow USB debugging** prompt inside the headset.

### Troubleshooting

- **Unauthorized**: Put on the headset/device and accept the USB debugging prompt.
- **Offline**: Replug the USB cable or toggle USB debugging in the headset settings.
- **No devices listed**:
  - Ensure USB debugging is enabled.
  - Try a different USB cable/port.
  - Verify that the headset is powered on and awake.
- **gnirehtet errors**: Check the log panel for the exact error output.

## Build & package (single-file EXE)

### One-command build

```powershell
./build.ps1
```

This produces a single, self-contained EXE at:

```
./dist/TetherMate.exe
```

### Manual build command

```powershell
dotnet publish src/TetherMate/TetherMate.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeAllContentForSelfExtract=true -o dist
```

## Compatibility notes

- The app targets Windows 10/11 and uses WPF for a native UI.
- The `gnirehtet` CLI is invoked with the `-s <serial>` argument to target the selected device.

## Built with

- C# / .NET 8
- WPF (Windows Presentation Foundation)
- `adb` + `gnirehtet`
