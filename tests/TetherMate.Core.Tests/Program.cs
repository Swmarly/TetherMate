using System;
using System.Collections.Generic;
using System.Linq;
using TetherMate.Core.Models;
using TetherMate.Core.Services;

namespace TetherMate.Core.Tests;

internal static class Program
{
    private static readonly (string Name, Action Test)[] Tests =
    [
        ("parses authorized ADB devices", ParseAuthorizedDevice),
        ("parses unauthorized ADB devices", ParseUnauthorizedDevice),
        ("ignores ADB daemon noise", IgnoreDaemonNoise),
        ("parses Android properties", ParseProperties),
        ("detects the gnirehtet reverse tunnel", DetectTunnel),
        ("uses gnirehtet positional serial syntax", BuildGnirehtetCommands),
        ("tracks relay client lifecycle lines", ParseRelayLifecycle),
    ];

    public static int Main()
    {
        var failures = 0;
        foreach (var (name, test) in Tests)
        {
            try
            {
                test();
                Console.WriteLine($"PASS  {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL  {name}: {exception.Message}");
            }
        }

        Console.WriteLine($"{Tests.Length - failures}/{Tests.Length} tests passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void ParseAuthorizedDevice()
    {
        const string output = """
            List of devices attached
            1WMHH123456789 device product:hollywood model:Quest_3 device:hollywood usb:1-2 transport_id:4
            """;
        var device = Single(AdbOutputParser.ParseDevices(output));
        Equal("1WMHH123456789", device.Serial);
        Equal(AdbDeviceState.Ready, device.State);
        Equal("hollywood", device.Product);
        Equal("Quest 3", device.Model);
        Equal("1-2", device.UsbLocation);
    }

    private static void ParseUnauthorizedDevice()
    {
        const string output = """
            List of devices attached
            1WMHH123456789 unauthorized usb:1-2 transport_id:1
            """;
        var device = Single(AdbOutputParser.ParseDevices(output));
        Equal(AdbDeviceState.Unauthorized, device.State);
        False(device.IsReady, "An unauthorized device must not be ready.");
    }

    private static void IgnoreDaemonNoise()
    {
        const string output = """
            * daemon not running; starting now at tcp:5037
            * daemon started successfully
            List of devices attached

            """;
        Equal(0, AdbOutputParser.ParseDevices(output).Count);
    }

    private static void ParseProperties()
    {
        const string output = """
            [ro.product.manufacturer]: [Oculus]
            [ro.product.model]: [Quest 3]
            [ro.product.name]: [hollywood]
            """;
        var properties = AdbOutputParser.ParseGetProp(output);
        Equal("Oculus", properties["ro.product.manufacturer"]);
        Equal("Quest 3", properties["ro.product.model"]);
    }

    private static void DetectTunnel()
    {
        const string output = "1WMHH123456789 localabstract:gnirehtet tcp:31416";
        True(AdbOutputParser.ContainsGnirehtetTunnel(output), "Expected the tunnel to be detected.");
        False(
            AdbOutputParser.ContainsGnirehtetTunnel("serial tcp:8080 tcp:8080"),
            "An unrelated reverse mapping must not count.");
    }

    private static void BuildGnirehtetCommands()
    {
        SequenceEqual(["start", "serial with spaces"], GnirehtetCli.Start("serial with spaces"));
        SequenceEqual(["stop", "abc"], GnirehtetCli.Stop("abc"));
        False(GnirehtetCli.Start("abc").Contains("-s"), "gnirehtet does not accept adb-style -s syntax.");
    }

    private static void ParseRelayLifecycle()
    {
        var connected = RelayLogParser.Parse("2026-08-22 INFO TunnelServer: Client #7 connected");
        Equal(RelayLogEventKind.ClientConnected, connected.Kind);
        Equal(7, connected.ClientId);

        var disconnected = RelayLogParser.Parse("INFO TunnelServer: Client #7 disconnected");
        Equal(RelayLogEventKind.ClientDisconnected, disconnected.Kind);
        Equal(7, disconnected.ClientId);
    }

    private static T Single<T>(IReadOnlyList<T> values)
    {
        Equal(1, values.Count);
        return values[0];
    }

    private static void True(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void False(bool value, string message) => True(!value, message);

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
        }
    }

    private static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                $"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
        }
    }
}
