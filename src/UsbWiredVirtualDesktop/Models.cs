using System;
using System.Linq;

namespace UsbWiredVirtualDesktop;

public sealed class DeviceInfo
{
    public string Serial { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public string Product { get; set; } = "";
    public string Device { get; set; } = "";
    public string TransportId { get; set; } = "";
    public bool IsResponsive { get; set; }
    public DateTimeOffset LastProbe { get; set; } = DateTimeOffset.MinValue;

    public bool IsReady => State.Equals("device", StringComparison.OrdinalIgnoreCase) && IsResponsive;

    public string DisplayName
    {
        get
        {
            var friendly = string.Join(" ", new[] { Manufacturer, Model }.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (string.IsNullOrWhiteSpace(friendly))
            {
                friendly = Product;
            }

            return string.IsNullOrWhiteSpace(friendly)
                ? Serial
                : $"{friendly} ({Serial})";
        }
    }
}

public sealed class LogEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public string Message { get; init; } = string.Empty;

    public string Display => $"[{Timestamp:HH:mm:ss}] {Message}";
}
