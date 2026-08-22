using System.Text;

namespace TetherMate.Infrastructure;

public sealed class FileLogger
{
    private const long MaxLogBytes = 2 * 1024 * 1024;
    private readonly object _writeLock = new();

    public FileLogger(string? logDirectory = null)
    {
        LogDirectory = logDirectory ?? AppPaths.LogsDirectory;
        LogPath = Path.Combine(LogDirectory, "TetherMate.log");
    }

    public string LogDirectory { get; }

    public string LogPath { get; }

    public void Write(string message)
    {
        try
        {
            lock (_writeLock)
            {
                Directory.CreateDirectory(LogDirectory);
                RotateIfNeeded();
                var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}  {message}{Environment.NewLine}";
                File.AppendAllText(LogPath, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(LogPath) || new FileInfo(LogPath).Length < MaxLogBytes)
        {
            return;
        }

        for (var index = 2; index >= 1; index--)
        {
            var source = index == 1 ? LogPath : $"{LogPath}.{index - 1}";
            var destination = $"{LogPath}.{index}";
            if (File.Exists(source))
            {
                File.Move(source, destination, overwrite: true);
            }
        }
    }
}
