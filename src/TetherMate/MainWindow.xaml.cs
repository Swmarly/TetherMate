using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TetherMate;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Closing += (_, _) => _viewModel.RequestShutdown();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyDarkTitleBar();
        await _viewModel.InitializeAsync();
    }

    private void ApplyDarkTitleBar()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        const int dwmwaUseImmersiveDarkMode = 20;
        const int dwmwaUseImmersiveDarkModeBefore20H1 = 19;

        var useImmersiveDarkMode = 1;
        _ = DwmSetWindowAttribute(hwnd, dwmwaUseImmersiveDarkMode, ref useImmersiveDarkMode, Marshal.SizeOf<int>());
        _ = DwmSetWindowAttribute(hwnd, dwmwaUseImmersiveDarkModeBefore20H1, ref useImmersiveDarkMode, Marshal.SizeOf<int>());
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int pvAttribute,
        int cbAttribute);
}
