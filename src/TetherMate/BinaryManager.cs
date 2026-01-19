using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace TetherMate;

public sealed class BinaryManager
{
    private readonly string _binDirectory;

    public BinaryManager(string binDirectory)
    {
        _binDirectory = binDirectory;
    }

    public string AdbPath => Path.Combine(_binDirectory, "adb.exe");
    public string GnirehtetPath => Path.Combine(_binDirectory, "gnirehtet.exe");

    public async Task EnsureExtractedAsync(Action<string> log)
    {
        Directory.CreateDirectory(_binDirectory);
        var resources = new Dictionary<string, string>
        {
            ["TetherMate.Resources.adb.exe"] = AdbPath,
            ["TetherMate.Resources.AdbWinApi.dll"] = Path.Combine(_binDirectory, "AdbWinApi.dll"),
            ["TetherMate.Resources.AdbWinUsbApi.dll"] = Path.Combine(_binDirectory, "AdbWinUsbApi.dll"),
            ["TetherMate.Resources.gnirehtet.exe"] = GnirehtetPath,
            ["TetherMate.Resources.gnirehtet.apk"] = Path.Combine(_binDirectory, "gnirehtet.apk"),
            ["TetherMate.Resources.libwinpthread-1.dll"] = Path.Combine(_binDirectory, "libwinpthread-1.dll"),
        };

        foreach (var (resourceName, outputPath) in resources)
        {
            await ExtractIfNeededAsync(resourceName, outputPath, _binDirectory, log);
        }
    }

    private static async Task ExtractIfNeededAsync(string resourceName, string outputPath, string binDirectory, Action<string> log)
    {
        var assembly = Assembly.GetExecutingAssembly();
        await using var resource = assembly.GetManifestResourceStream(resourceName);
        if (resource is null)
        {
            log($"Missing embedded resource: {resourceName}");
            return;
        }

        var shouldWrite = true;
        if (File.Exists(outputPath))
        {
            var existingLength = new FileInfo(outputPath).Length;
            shouldWrite = existingLength != resource.Length;
        }

        if (!shouldWrite)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await using var fileStream = File.Create(outputPath);
        await resource.CopyToAsync(fileStream);
        log($"Extracted {Path.GetFileName(outputPath)} to {binDirectory}");
    }
}
