namespace TetherMate.Core.Services;

public static class GnirehtetCli
{
    public static IReadOnlyList<string> Relay() => ["relay"];

    // gnirehtet uses a positional serial after the command. It does not accept "-s".
    public static IReadOnlyList<string> Start(string serial) => ["start", serial];

    public static IReadOnlyList<string> Stop(string serial) => ["stop", serial];

    public static IReadOnlyList<string> Reinstall(string serial) => ["reinstall", serial];
}
