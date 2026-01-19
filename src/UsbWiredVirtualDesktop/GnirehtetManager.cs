using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace UsbWiredVirtualDesktop;

public sealed class GnirehtetManager
{
    private readonly string _gnirehtetPath;
    private readonly Action<string> _log;
    private Process? _process;

    public GnirehtetManager(string gnirehtetPath, Action<string> log)
    {
        _gnirehtetPath = gnirehtetPath;
        _log = log;
    }

    public bool IsRunning => _process is { HasExited: false };

    public async Task StartAsync(string serial)
    {
        if (IsRunning)
        {
            _log("gnirehtet already running.");
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = _gnirehtetPath,
            Arguments = $"run -s {serial}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                _log($"gnirehtet: {args.Data}");
            }
        };
        _process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                _log($"gnirehtet error: {args.Data}");
            }
        };
        _process.Exited += (_, _) =>
        {
            _log("gnirehtet exited.");
        };

        try
        {
            if (!_process.Start())
            {
                _log("gnirehtet failed to start.");
                _process = null;
                return;
            }
        }
        catch (Exception ex)
        {
            _log($"gnirehtet failed to start: {ex.Message}");
            _process = null;
            return;
        }

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        _log("gnirehtet starting...");
        await Task.CompletedTask;
    }

    public async Task StopAsync(string serial)
    {
        await StopProcessAsync();
        await RunGnirehtetCommandAsync($"stop -s {serial}");
    }

    public async Task RestartAsync(string serial)
    {
        await StopAsync(serial);
        await StartAsync(serial);
    }

    public async Task CleanupOrphansAsync()
    {
        foreach (var process in Process.GetProcessesByName("gnirehtet"))
        {
            using (process)
            {
                try
                {
                    process.Kill(true);
                    _log("Killed orphaned gnirehtet process.");
                }
                catch (Exception ex)
                {
                    _log($"Failed to kill orphaned gnirehtet process: {ex.Message}");
                }
            }
        }

        await Task.CompletedTask;
    }

    private async Task StopProcessAsync()
    {
        if (_process is { HasExited: false } runningProcess)
        {
            try
            {
                runningProcess.Kill(true);
                _log("gnirehtet process terminated.");
            }
            catch (Exception ex)
            {
                _log($"Failed to terminate gnirehtet: {ex.Message}");
            }
            finally
            {
                runningProcess.Dispose();
            }
        }
        else
        {
            _process?.Dispose();
        }

        _process = null;
        await Task.CompletedTask;
    }

    private async Task RunGnirehtetCommandAsync(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _gnirehtetPath,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            return;
        }

        await process.WaitForExitAsync();
    }
}
