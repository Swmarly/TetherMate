namespace TetherMate.Core.Models;

public enum AdbDeviceState
{
    Ready,
    Unauthorized,
    Offline,
    Recovery,
    NoPermissions,
    Unknown,
}

public sealed record AndroidDevice(
    string Serial,
    string RawState,
    string Manufacturer = "",
    string Model = "",
    string Product = "",
    string Device = "",
    string TransportId = "",
    string UsbLocation = "",
    bool IsResponsive = false)
{
    public AdbDeviceState State => RawState.ToLowerInvariant() switch
    {
        "device" => AdbDeviceState.Ready,
        "unauthorized" => AdbDeviceState.Unauthorized,
        "offline" => AdbDeviceState.Offline,
        "recovery" => AdbDeviceState.Recovery,
        "no" => AdbDeviceState.NoPermissions,
        _ => AdbDeviceState.Unknown,
    };

    public bool IsReady => State == AdbDeviceState.Ready && IsResponsive;

    public string FriendlyName
    {
        get
        {
            var parts = new[] { Manufacturer, Model }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            var name = string.Join(" ", parts);

            if (string.IsNullOrWhiteSpace(name))
            {
                name = !string.IsNullOrWhiteSpace(Product) ? Product : "Android device";
            }

            return name;
        }
    }

    public string DisplayName => $"{FriendlyName}  ·  {Serial}";
}
