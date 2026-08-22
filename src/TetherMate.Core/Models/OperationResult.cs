namespace TetherMate.Core.Models;

public sealed record OperationResult(bool Succeeded, string Message)
{
    public static OperationResult Success(string message) => new(true, message);

    public static OperationResult Failure(string message) => new(false, message);
}

public sealed record SessionSnapshot(
    bool IsRelayRunning,
    bool IsClientConnected,
    string? ActiveSerial,
    string? LastError)
{
    public static SessionSnapshot Stopped { get; } = new(false, false, null, null);
}

public sealed record RuntimeBundle(
    string Directory,
    string AdbPath,
    string GnirehtetPath,
    string GnirehtetApkPath);
