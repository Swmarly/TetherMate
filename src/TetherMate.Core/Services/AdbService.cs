using TetherMate.Core.Models;

namespace TetherMate.Core.Services;

public sealed class AdbService
{
    private static readonly TimeSpan IdentityCacheLifetime = TimeSpan.FromSeconds(30);
    private readonly string _adbPath;
    private readonly string _workingDirectory;
    private readonly IProcessRunner _processRunner;
    private readonly Action<string> _log;
    private readonly Dictionary<string, CachedIdentity> _identityCache = new(StringComparer.Ordinal);

    public AdbService(
        string adbPath,
        IProcessRunner processRunner,
        Action<string> log)
    {
        _adbPath = adbPath;
        _workingDirectory = Path.GetDirectoryName(adbPath)
            ?? throw new ArgumentException("ADB path must have a parent directory.", nameof(adbPath));
        _processRunner = processRunner;
        _log = log;
    }

    public async Task<OperationResult> EnsureServerAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["start-server"], TimeSpan.FromSeconds(10), cancellationToken);
        if (!result.Succeeded)
        {
            return OperationResult.Failure(CleanError(result.BestError));
        }

        return OperationResult.Success("ADB is available.");
    }

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["version"], TimeSpan.FromSeconds(5), cancellationToken);
        return result.Succeeded ? FirstUsefulLine(result.StandardOutput) : "Unavailable";
    }

    public async Task<IReadOnlyList<AndroidDevice>> GetDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["devices", "-l"], TimeSpan.FromSeconds(8), cancellationToken);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"ADB device scan failed: {CleanError(result.BestError)}");
        }

        var parsed = AdbOutputParser.ParseDevices(result.StandardOutput);
        var visibleSerials = parsed.Select(device => device.Serial).ToHashSet(StringComparer.Ordinal);
        foreach (var cachedSerial in _identityCache.Keys.Where(serial => !visibleSerials.Contains(serial)).ToArray())
        {
            _identityCache.Remove(cachedSerial);
        }

        var enriched = new List<AndroidDevice>(parsed.Count);
        foreach (var device in parsed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            enriched.Add(device.State == AdbDeviceState.Ready
                ? await EnrichDeviceAsync(device, cancellationToken)
                : device);
        }

        return enriched;
    }

    public async Task<bool> HasGnirehtetTunnelAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            ["-s", serial, "reverse", "--list"],
            TimeSpan.FromSeconds(5),
            cancellationToken);

        return result.Succeeded && AdbOutputParser.ContainsGnirehtetTunnel(result.StandardOutput);
    }

    public async Task RemoveGnirehtetTunnelAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            ["-s", serial, "reverse", "--remove", "localabstract:gnirehtet"],
            TimeSpan.FromSeconds(5),
            cancellationToken);

        if (!result.Succeeded)
        {
            _log($"Could not remove the old USB tunnel: {CleanError(result.BestError)}");
        }
    }

    private async Task<AndroidDevice> EnrichDeviceAsync(
        AndroidDevice device,
        CancellationToken cancellationToken)
    {
        if (_identityCache.TryGetValue(device.Serial, out var cached) &&
            DateTimeOffset.UtcNow - cached.FetchedAt < IdentityCacheLifetime)
        {
            return ApplyIdentity(device, cached.Device);
        }

        var result = await RunAsync(
            ["-s", device.Serial, "shell", "getprop"],
            TimeSpan.FromSeconds(8),
            cancellationToken);

        if (!result.Succeeded)
        {
            return device with { IsResponsive = false };
        }

        var properties = AdbOutputParser.ParseGetProp(result.StandardOutput);
        var enriched = device with
        {
            Manufacturer = GetProperty(properties, "ro.product.manufacturer"),
            Model = FirstProperty(properties, "ro.product.model", "ro.product.vendor.model"),
            Product = FirstNonEmpty(device.Product, GetProperty(properties, "ro.product.name")),
            Device = FirstNonEmpty(device.Device, GetProperty(properties, "ro.product.device")),
            IsResponsive = properties.Count > 0,
        };

        if (enriched.IsResponsive)
        {
            _identityCache[device.Serial] = new CachedIdentity(enriched, DateTimeOffset.UtcNow);
        }

        return enriched;
    }

    private static AndroidDevice ApplyIdentity(AndroidDevice device, AndroidDevice identity) =>
        device with
        {
            Manufacturer = identity.Manufacturer,
            Model = identity.Model,
            Product = FirstNonEmpty(device.Product, identity.Product),
            Device = FirstNonEmpty(device.Device, identity.Device),
            IsResponsive = true,
        };

    private Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return _processRunner.RunAsync(
            new ProcessRequest(_adbPath, arguments, _workingDirectory),
            timeout,
            cancellationToken);
    }

    private static string GetProperty(IReadOnlyDictionary<string, string> properties, string key) =>
        properties.TryGetValue(key, out var value) ? value.Trim() : "";

    private static string FirstProperty(
        IReadOnlyDictionary<string, string> properties,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = GetProperty(properties, key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return "";
    }

    private static string FirstNonEmpty(string first, string second) =>
        !string.IsNullOrWhiteSpace(first) ? first : second;

    private static string FirstUsefulLine(string value) =>
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0)
        ?? "Unavailable";

    private static string CleanError(string value)
    {
        var lines = value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Take(3);
        return string.Join(" ", lines);
    }

    private sealed record CachedIdentity(AndroidDevice Device, DateTimeOffset FetchedAt);
}
