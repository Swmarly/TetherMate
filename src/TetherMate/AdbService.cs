using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TetherMate;

public sealed class AdbService
{
    private readonly string _adbPath;
    private readonly Action<string> _log;

    public AdbService(string adbPath, Action<string> log)
    {
        _adbPath = adbPath;
        _log = log;
    }

    public async Task<bool> EnsureServerAsync()
    {
        var result = await RunAdbAsync("start-server", TimeSpan.FromSeconds(5));
        if (result.ExitCode != 0)
        {
            _log($"ADB start-server failed: {result.StandardError.Trim()}");
        }

        return result.ExitCode == 0;
    }

    public async Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync()
    {
        var result = await RunAdbAsync("devices -l", TimeSpan.FromSeconds(5));
        var devices = new List<DeviceInfo>();
        if (result.ExitCode != 0)
        {
            _log($"ADB devices failed: {result.StandardError.Trim()}");
            return devices;
        }

        var lines = result.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines.Skip(1))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            var segments = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2)
            {
                continue;
            }

            var serialAndState = segments[0..2];
            var info = new DeviceInfo
            {
                Serial = serialAndState[0],
                State = serialAndState[1]
            };

            foreach (var segment in segments.Skip(2))
            {
                var kvp = segment.Split(':', 2);
                if (kvp.Length != 2)
                {
                    continue;
                }

                switch (kvp[0])
                {
                    case "product":
                        info.Product = kvp[1];
                        break;
                    case "device":
                        info.Device = kvp[1];
                        break;
                    case "transport_id":
                        info.TransportId = kvp[1];
                        break;
                }
            }

            devices.Add(info);
        }

        return devices;
    }

    public async Task<bool> ProbeDeviceAsync(DeviceInfo device)
    {
        if (!device.State.Equals("device", StringComparison.OrdinalIgnoreCase))
        {
            device.IsResponsive = false;
            return false;
        }

        if (DateTimeOffset.Now - device.LastProbe < TimeSpan.FromSeconds(4))
        {
            return device.IsResponsive;
        }

        var manufacturer = await GetPropAsync(device.Serial, "ro.product.manufacturer");
        var model = await GetPropAsync(device.Serial, "ro.product.model");

        device.Manufacturer = manufacturer.Trim();
        device.Model = model.Trim();
        device.IsResponsive = !(string.IsNullOrWhiteSpace(manufacturer) && string.IsNullOrWhiteSpace(model));
        device.LastProbe = DateTimeOffset.Now;
        return device.IsResponsive;
    }

    private async Task<string> GetPropAsync(string serial, string prop)
    {
        var result = await RunAdbAsync($"-s {serial} shell getprop {prop}", TimeSpan.FromSeconds(4));
        return result.ExitCode == 0 ? result.StandardOutput : string.Empty;
    }

    private async Task<ProcessResult> RunAdbAsync(string arguments, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _adbPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        var output = new StringBuilder();
        var error = new StringBuilder();
        var outputReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is null)
            {
                outputReady.TrySetResult(true);
            }
            else
            {
                output.AppendLine(args.Data);
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is null)
            {
                errorReady.TrySetResult(true);
            }
            else
            {
                error.AppendLine(args.Data);
            }
        };

        try
        {
            if (!process.Start())
            {
                return new ProcessResult { ExitCode = -1 };
            }
        }
        catch (Exception ex)
        {
            return new ProcessResult { ExitCode = -1, StandardError = ex.Message };
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(true);
            }
            catch (InvalidOperationException)
            {
            }
        }
        finally
        {
            outputReady.TrySetResult(true);
            errorReady.TrySetResult(true);
        }

        await Task.WhenAll(outputReady.Task, errorReady.Task);

        return new ProcessResult
        {
            ExitCode = process.HasExited ? process.ExitCode : -1,
            StandardOutput = output.ToString(),
            StandardError = error.ToString(),
        };
    }
}

public sealed class ProcessResult
{
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;
}
