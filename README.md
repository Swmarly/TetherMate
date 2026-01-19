# TetherMate

TetherMate lets you use a **wired** network connection over the USB cable on your VR headset. It creates a USB-based Wi‑Fi link so VR streaming apps like **Virtual Desktop** (normally wireless) can run over a stable, low-latency **wired** connection instead of regular Wi‑Fi. This is especially useful when you want the reliability of a cable while still using Wi‑Fi–based streaming features.

A Windows 10/11 desktop application that automatically manages `adb` + `gnirehtet` reverse tethering for compatible Android-based VR headsets (Meta Quest 2 and similar). The app watches connected ADB devices, selects a target device, and keeps `gnirehtet` running only when the device is ready.

## Table of contents

- [About the project](#about-the-project)
  - [Built with](#built-with)
- [How it works](#how-it-works)
- [Getting started](#getting-started)
  - [Prerequisites](#prerequisites)
- [Usage](#usage)
  - [Selecting a target device](#selecting-a-target-device)
  - [Required headset settings (ADB debugging)](#required-headset-settings-adb-debugging)
  - [Troubleshooting](#troubleshooting)
- [Build & package (single-file EXE)](#build--package-single-file-exe)
- [Notes on compatibility](#notes-on-compatibility)

## About the project

TetherMate provides a stable, low-latency wired alternative for VR streaming apps by managing reverse tethering over USB. It bundles `adb` and `gnirehtet`, detects ready devices, and automatically starts or stops the tethering session based on headset state.

### Built with

- C# / .NET 8
- WPF (Windows Presentation Foundation)
- `adb` + `gnirehtet`

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

## Getting started

### Prerequisites

- Windows 10/11
- .NET 8 SDK (build-time only)

## Usage

### Selecting a target device

1. Connect your headset/device via USB and accept the ADB authorization prompt inside the headset.
2. The device will appear in the **Connected ADB devices** list.
3. Use the **Target device** dropdown to select the device to manage.
4. Manual **Start / Stop / Restart** controls are provided for overrides.

### Required headset settings (ADB debugging)

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

## Notes on compatibility

- The app targets Windows 10/11 and uses WPF for a native UI.
- The `gnirehtet` CLI is invoked with the `-s <serial>` argument to target the selected device.
