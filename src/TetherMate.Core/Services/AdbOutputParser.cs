using System.Text.RegularExpressions;
using TetherMate.Core.Models;

namespace TetherMate.Core.Services;

public static class AdbOutputParser
{
    private static readonly Regex WhitespaceRegex = new("\\s+", RegexOptions.Compiled);
    private static readonly Regex GetPropRegex = new(
        "^\\[(?<key>[^]]+)]\\s*:\\s*\\[(?<value>.*)]$",
        RegexOptions.Compiled);

    public static IReadOnlyList<AndroidDevice> ParseDevices(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var devices = new List<AndroidDevice>();
        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 ||
                line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith('*'))
            {
                continue;
            }

            var segments = WhitespaceRegex.Split(line);
            if (segments.Length < 2)
            {
                continue;
            }

            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var segment in segments.Skip(2))
            {
                var separator = segment.IndexOf(':');
                if (separator <= 0 || separator == segment.Length - 1)
                {
                    continue;
                }

                metadata[segment[..separator]] = segment[(separator + 1)..];
            }

            devices.Add(new AndroidDevice(
                segments[0],
                segments[1],
                Model: Get(metadata, "model").Replace('_', ' '),
                Product: Get(metadata, "product"),
                Device: Get(metadata, "device"),
                TransportId: Get(metadata, "transport_id"),
                UsbLocation: Get(metadata, "usb")));
        }

        return devices;
    }

    public static IReadOnlyDictionary<string, string> ParseGetProp(string output)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = GetPropRegex.Match(rawLine.Trim());
            if (match.Success)
            {
                properties[match.Groups["key"].Value] = match.Groups["value"].Value;
            }
        }

        return properties;
    }

    public static bool ContainsGnirehtetTunnel(string output)
    {
        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.Contains("localabstract:gnirehtet", StringComparison.OrdinalIgnoreCase) &&
                         line.Contains("tcp:31416", StringComparison.OrdinalIgnoreCase));
    }

    private static string Get(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : "";

}
