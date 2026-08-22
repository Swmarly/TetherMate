using System.Collections.Concurrent;
using System.Diagnostics;
using TetherMate.Core.Models;

namespace TetherMate.Core.Services;

public sealed class GnirehtetSession : IAsyncDisposable
{
    private readonly RuntimeBundle _runtime;
    private readonly AdbService _adbService;
    private readonly IProcessRunner _processRunner;
    private readonly Action<string> _log;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _stateLock = new();
    private readonly HashSet<int> _connectedClients = [];
    private readonly ConcurrentQueue<string> _relayOutput = new();
    private Process? _relayProcess;
    private SessionSnapshot _snapshot = SessionSnapshot.Stopped;
    private bool _disposed;

    public GnirehtetSession(
        RuntimeBundle runtime,
        AdbService adbService,
        IProcessRunner processRunner,
        Action<string> log)
    {
        _runtime = runtime;
        _adbService = adbService;
        _processRunner = processRunner;
        _log = log;
    }

    public event EventHandler? StateChanged;

    public SessionSnapshot Snapshot
    {
        get
        {
            lock (_stateLock)
            {
                return _snapshot;
            }
        }
    }

    public async Task<OperationResult> StartAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = Snapshot;
            if (current.IsRelayRunning && current.ActiveSerial == serial)
            {
                return OperationResult.Success(current.IsClientConnected
                    ? "The wired link is already connected."
                    : "The wired link is waiting for the headset VPN.");
            }

            if (current.IsRelayRunning || current.ActiveSerial is not null)
            {
                await StopCoreAsync(CancellationToken.None).ConfigureAwait(false);
            }

            SetSnapshot(new SessionSnapshot(false, false, null, null));
            var relayResult = await StartRelayAsync(cancellationToken).ConfigureAwait(false);
            if (!relayResult.Succeeded)
            {
                SetSnapshot(new SessionSnapshot(false, false, null, relayResult.Message));
                return relayResult;
            }

            SetSnapshot(new SessionSnapshot(true, false, serial, null));

            _log($"Preparing reverse tethering for {serial}...");
            var startResult = await RunGnirehtetAsync(
                GnirehtetCli.Start(serial),
                TimeSpan.FromSeconds(45),
                cancellationToken).ConfigureAwait(false);
            LogCommandOutput(startResult);

            if (!startResult.Succeeded)
            {
                var message = $"gnirehtet could not start: {CleanError(startResult.BestError)}";
                await StopCoreAsync(CancellationToken.None).ConfigureAwait(false);
                SetSnapshot(new SessionSnapshot(false, false, null, message));
                return OperationResult.Failure(message);
            }

            var hasTunnel = await _adbService.HasGnirehtetTunnelAsync(serial, cancellationToken)
                .ConfigureAwait(false);
            if (!hasTunnel)
            {
                const string message = "gnirehtet started, but ADB did not create the USB tunnel.";
                await StopCoreAsync(CancellationToken.None).ConfigureAwait(false);
                SetSnapshot(new SessionSnapshot(false, false, null, message));
                return OperationResult.Failure(message);
            }

            lock (_stateLock)
            {
                _snapshot = new SessionSnapshot(
                    IsRelayRunning: _relayProcess is { HasExited: false },
                    IsClientConnected: _connectedClients.Count > 0,
                    ActiveSerial: serial,
                    LastError: null);
            }

            RaiseStateChanged();
            _log("USB tunnel created. Accept the VPN prompt inside the headset if it appears.");
            return OperationResult.Success("USB tunnel created; waiting for the headset client.");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<OperationResult> StopAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync(cancellationToken).ConfigureAwait(false);
            return OperationResult.Success("The wired link is off.");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<OperationResult> RestartAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);
        return await StartAsync(serial, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult> ReinstallClientAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync(CancellationToken.None).ConfigureAwait(false);
            _log("Reinstalling the gnirehtet client on the headset...");
            var result = await RunGnirehtetAsync(
                GnirehtetCli.Reinstall(serial),
                TimeSpan.FromSeconds(60),
                cancellationToken).ConfigureAwait(false);
            LogCommandOutput(result);
            return result.Succeeded
                ? OperationResult.Success("The headset client was reinstalled.")
                : OperationResult.Failure($"Reinstall failed: {CleanError(result.BestError)}");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<OperationResult> StartRelayAsync(CancellationToken cancellationToken)
    {
        while (_relayOutput.TryDequeue(out _))
        {
        }

        lock (_stateLock)
        {
            _connectedClients.Clear();
        }

        var request = CreateRequest(GnirehtetCli.Relay());
        var startInfo = ProcessRunner.CreateStartInfo(request);
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += OnRelayOutput;
        process.ErrorDataReceived += OnRelayOutput;
        process.Exited += OnRelayExited;

        try
        {
            if (!process.Start())
            {
                process.Dispose();
                return OperationResult.Failure("The gnirehtet relay could not be started.");
            }

            _relayProcess = process;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await Task.Delay(TimeSpan.FromMilliseconds(700), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await StopRelayAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            TryKill(process);
            _relayProcess = null;
            process.Dispose();
            return OperationResult.Failure($"The gnirehtet relay could not start: {exception.Message}");
        }

        if (process.HasExited)
        {
            var error = string.Join(" ", _relayOutput.TakeLast(4));
            process.Dispose();
            _relayProcess = null;
            return OperationResult.Failure(string.IsNullOrWhiteSpace(error)
                ? "The gnirehtet relay exited immediately. Port 31416 may already be in use."
                : CleanError(error));
        }

        SetSnapshot(new SessionSnapshot(true, false, null, null));
        _log("gnirehtet relay is listening on the PC.");
        return OperationResult.Success("Relay started.");
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        var serial = Snapshot.ActiveSerial;
        try
        {
            if (!string.IsNullOrWhiteSpace(serial))
            {
                var stopResult = await RunGnirehtetAsync(
                    GnirehtetCli.Stop(serial),
                    TimeSpan.FromSeconds(12),
                    cancellationToken).ConfigureAwait(false);
                LogCommandOutput(stopResult);
            }
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(serial))
            {
                await _adbService.RemoveGnirehtetTunnelAsync(serial, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            await StopRelayAsync().ConfigureAwait(false);
            SetSnapshot(SessionSnapshot.Stopped);
            _log("Wired link stopped.");
        }
    }

    private async Task StopRelayAsync()
    {
        var process = _relayProcess;
        _relayProcess = null;
        if (process is null)
        {
            return;
        }

        process.OutputDataReceived -= OnRelayOutput;
        process.ErrorDataReceived -= OnRelayOutput;
        process.Exited -= OnRelayExited;

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            _log("The relay took too long to close.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _log($"Could not close the relay cleanly: {exception.Message}");
        }
        finally
        {
            process.Dispose();
            lock (_stateLock)
            {
                _connectedClients.Clear();
            }
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private Task<ProcessResult> RunGnirehtetAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return _processRunner.RunAsync(CreateRequest(arguments), timeout, cancellationToken);
    }

    private ProcessRequest CreateRequest(IReadOnlyList<string> arguments)
    {
        var environment = new Dictionary<string, string?>
        {
            ["ADB"] = _runtime.AdbPath,
            ["GNIREHTET_APK"] = _runtime.GnirehtetApkPath,
        };

        return new ProcessRequest(
            _runtime.GnirehtetPath,
            arguments,
            _runtime.Directory,
            environment);
    }

    private void OnRelayOutput(object sender, DataReceivedEventArgs eventArgs)
    {
        if (string.IsNullOrWhiteSpace(eventArgs.Data))
        {
            return;
        }

        var line = eventArgs.Data.Trim();
        _relayOutput.Enqueue(line);
        while (_relayOutput.Count > 40 && _relayOutput.TryDequeue(out _))
        {
        }

        _log($"relay · {line}");
        var parsed = RelayLogParser.Parse(line);
        var changed = false;
        lock (_stateLock)
        {
            switch (parsed.Kind)
            {
                case RelayLogEventKind.ClientConnected when parsed.ClientId.HasValue:
                    changed = _connectedClients.Add(parsed.ClientId.Value);
                    break;
                case RelayLogEventKind.ClientDisconnected when parsed.ClientId.HasValue:
                    changed = _connectedClients.Remove(parsed.ClientId.Value);
                    break;
            }

            if (changed)
            {
                _snapshot = _snapshot with { IsClientConnected = _connectedClients.Count > 0 };
            }
        }

        if (changed)
        {
            RaiseStateChanged();
        }
    }

    private void OnRelayExited(object? sender, EventArgs eventArgs)
    {
        string message;
        lock (_stateLock)
        {
            _connectedClients.Clear();
            message = _snapshot.ActiveSerial is null
                ? "The gnirehtet relay stopped."
                : "The gnirehtet relay exited unexpectedly.";
            _snapshot = new SessionSnapshot(false, false, _snapshot.ActiveSerial, message);
        }

        _log(message);
        RaiseStateChanged();
    }

    private void LogCommandOutput(ProcessResult result)
    {
        foreach (var line in (result.StandardOutput + Environment.NewLine + result.StandardError)
                     .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                     .Select(line => line.Trim())
                     .Where(line => line.Length > 0))
        {
            _log($"gnirehtet · {line}");
        }
    }

    private void SetSnapshot(SessionSnapshot snapshot)
    {
        lock (_stateLock)
        {
            _snapshot = snapshot;
        }

        RaiseStateChanged();
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private static string CleanError(string value)
    {
        var lines = value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .TakeLast(4);
        return string.Join(" ", lines);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync(CancellationToken.None).ConfigureAwait(false);
            _disposed = true;
        }
        finally
        {
            _operationGate.Release();
            _operationGate.Dispose();
        }
    }
}
