using System.ComponentModel;
using System.Threading;
using System.Windows;
using TetherMate.Infrastructure;
using TetherMate.Presentation;

namespace TetherMate;

public partial class App : System.Windows.Application
{
    private const string MutexName = "Local\\TetherMate.SingleInstance.4E5336D5";
    private Mutex? _singleInstanceMutex;
    private MainWindow? _window;
    private MainViewModel? _viewModel;
    private TrayIconService? _trayIcon;
    private bool _isExiting;

    public new static App Current => (App)System.Windows.Application.Current;

    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);
        _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            System.Windows.MessageBox.Show(
                "TetherMate is already running. Check the notification area near the clock.",
                "TetherMate",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var logger = new FileLogger();
        _viewModel = new MainViewModel(
            Dispatcher,
            new BinaryManager(),
            new SettingsService(),
            logger);
        _window = new MainWindow(_viewModel);
        MainWindow = _window;
        _trayIcon = new TrayIconService(_viewModel, ShowMainWindow, ExitAsync);
        _window.Show();
    }

    public void HandleWindowClosing(CancelEventArgs eventArgs)
    {
        if (_isExiting || _window is null || _viewModel is null)
        {
            return;
        }

        eventArgs.Cancel = true;
        if (_viewModel.CloseToTray)
        {
            _window.Hide();
            _trayIcon?.ShowBackgroundHint();
            return;
        }

        _ = ExitAsync();
    }

    public void ShowMainWindow()
    {
        if (_window is null)
        {
            return;
        }

        Dispatcher.Invoke(_window.ShowAndActivate);
    }

    public async Task ExitAsync()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        _trayIcon?.Hide();
        try
        {
            if (_viewModel is not null)
            {
                await _viewModel.DisposeAsync();
            }
        }
        finally
        {
            _window?.Close();
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        _trayIcon?.Dispose();
        if (_singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            _singleInstanceMutex.Dispose();
        }

        base.OnExit(eventArgs);
    }
}
