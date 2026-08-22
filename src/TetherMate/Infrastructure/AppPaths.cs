namespace TetherMate.Infrastructure;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TetherMate");

    public static string RuntimeDirectory { get; } = Path.Combine(Root, "runtime");

    public static string LogsDirectory { get; } = Path.Combine(Root, "logs");

    public static string SettingsPath { get; } = Path.Combine(Root, "settings.json");
}
