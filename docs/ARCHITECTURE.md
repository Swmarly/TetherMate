# TetherMate architecture

## Connection lifecycle

TetherMate deliberately does not use `gnirehtet run`. That command starts the headset client on a background thread while the relay remains in the foreground, which makes a still-running process insufficient proof that startup succeeded.

Instead, `GnirehtetSession` owns a staged session:

1. Launch `gnirehtet relay` as a tracked child process.
2. Wait briefly and reject immediate exits such as a TCP 31416 bind failure.
3. Run `gnirehtet start <serial>` synchronously and check its exit status.
4. Verify `adb reverse --list` contains `localabstract:gnirehtet tcp:31416`.
5. Parse relay output for `Client #N connected` and `Client #N disconnected`.
6. Show Connected only while at least one tracked client is attached.

On stop, TetherMate asks the selected client to stop, removes only its own reverse mapping, and terminates only the relay process it created. It does not call `adb kill-server` and does not kill every process named `gnirehtet`.

## Runtime files

The Windows EXE embeds the tested ADB and gnirehtet bundle. On startup, `BinaryManager` computes the SHA-256 of every embedded resource and of any existing extracted file. A changed or corrupt file is written to a temporary file and atomically moved into place.

gnirehtet receives absolute paths through the upstream-supported environment variables:

- `ADB=%LOCALAPPDATA%\TetherMate\runtime\adb.exe`
- `GNIREHTET_APK=%LOCALAPPDATA%\TetherMate\runtime\gnirehtet.apk`

Its working directory is also set to the runtime directory so companion DLL lookup is deterministic.

## State and recovery

The UI state is derived from four independent signals:

- ADB server availability
- selected device state and responsiveness
- owned relay process lifetime
- relay client connection events

Automatic retries use bounded exponential backoff. A manual Disconnect sets an in-memory pause, preventing the old behavior where the monitor immediately reconnected against the user’s request. Selecting another device serializes teardown and startup through session gates.

## Tests and releases

The core tests cover ADB parsing, Android property parsing, tunnel detection, gnirehtet command construction, and relay lifecycle parsing. They use no NuGet test framework and run with `dotnet run` on any .NET 10 SDK.

`build.ps1` is the single source of truth for local builds, CI artifacts, and releases. A version tag or manual Release workflow creates the same tested package on GitHub’s Windows runner.
