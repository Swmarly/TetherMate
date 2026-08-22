using System.Reflection;
using System.Security.Cryptography;
using TetherMate.Core.Models;

namespace TetherMate.Infrastructure;

public sealed class BinaryManager
{
    private static readonly (string Resource, string FileName)[] EmbeddedFiles =
    [
        ("TetherMate.Resources.adb.exe", "adb.exe"),
        ("TetherMate.Resources.AdbWinApi.dll", "AdbWinApi.dll"),
        ("TetherMate.Resources.AdbWinUsbApi.dll", "AdbWinUsbApi.dll"),
        ("TetherMate.Resources.gnirehtet.exe", "gnirehtet.exe"),
        ("TetherMate.Resources.gnirehtet.apk", "gnirehtet.apk"),
        ("TetherMate.Resources.libwinpthread-1.dll", "libwinpthread-1.dll"),
        ("TetherMate.Resources.LICENSE.txt", "LICENSE.txt"),
        ("TetherMate.Resources.NOTICE.txt", "THIRD-PARTY-NOTICES.txt"),
        ("TetherMate.Resources.gnirehtet-LICENSE.txt", "gnirehtet-LICENSE.txt"),
    ];

    private readonly string _runtimeDirectory;

    public BinaryManager(string? runtimeDirectory = null)
    {
        _runtimeDirectory = runtimeDirectory ?? AppPaths.RuntimeDirectory;
    }

    public async Task<RuntimeBundle> EnsureRuntimeAsync(
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_runtimeDirectory);
        var assembly = Assembly.GetExecutingAssembly();

        foreach (var (resource, fileName) in EmbeddedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureFileAsync(assembly, resource, fileName, log, cancellationToken);
        }

        return new RuntimeBundle(
            _runtimeDirectory,
            Path.Combine(_runtimeDirectory, "adb.exe"),
            Path.Combine(_runtimeDirectory, "gnirehtet.exe"),
            Path.Combine(_runtimeDirectory, "gnirehtet.apk"));
    }

    public bool IsRuntimeComplete() => EmbeddedFiles.All(file =>
        File.Exists(Path.Combine(_runtimeDirectory, file.FileName)));

    private async Task EnsureFileAsync(
        Assembly assembly,
        string resourceName,
        string fileName,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        await using var resource = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded runtime file is missing: {resourceName}");

        await using var buffer = new MemoryStream();
        await resource.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        var expectedHash = SHA256.HashData(bytes);
        var targetPath = Path.Combine(_runtimeDirectory, fileName);

        if (File.Exists(targetPath))
        {
            await using var existing = File.OpenRead(targetPath);
            var existingHash = await SHA256.HashDataAsync(existing, cancellationToken);
            if (CryptographicOperations.FixedTimeEquals(expectedHash, existingHash))
            {
                return;
            }
        }

        var temporaryPath = targetPath + ".tmp";
        await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
        File.Move(temporaryPath, targetPath, overwrite: true);
        log($"Refreshed runtime file: {fileName}");
    }
}
