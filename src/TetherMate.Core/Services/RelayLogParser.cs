using System.Text.RegularExpressions;

namespace TetherMate.Core.Services;

public enum RelayLogEventKind
{
    None,
    RelayStarted,
    ClientConnected,
    ClientDisconnected,
}

public sealed record RelayLogEvent(RelayLogEventKind Kind, int? ClientId = null);

public static class RelayLogParser
{
    private static readonly Regex ClientRegex = new(
        "Client #(?<id>\\d+) (?<state>connected|disconnected)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static RelayLogEvent Parse(string line)
    {
        if (line.Contains("Starting relay server", StringComparison.OrdinalIgnoreCase))
        {
            return new RelayLogEvent(RelayLogEventKind.RelayStarted);
        }

        var match = ClientRegex.Match(line);
        if (!match.Success || !int.TryParse(match.Groups["id"].Value, out var clientId))
        {
            return new RelayLogEvent(RelayLogEventKind.None);
        }

        return match.Groups["state"].Value.Equals("connected", StringComparison.OrdinalIgnoreCase)
            ? new RelayLogEvent(RelayLogEventKind.ClientConnected, clientId)
            : new RelayLogEvent(RelayLogEventKind.ClientDisconnected, clientId);
    }

}
