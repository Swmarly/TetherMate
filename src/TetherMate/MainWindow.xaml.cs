using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using TetherMate.Presentation;

namespace TetherMate;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public void ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        await _viewModel.InitializeAsync();
    }

    private void OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        App.Current.HandleWindowClosing(eventArgs);
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Escape && _viewModel.CloseToTray)
        {
            Hide();
            eventArgs.Handled = true;
        }
    }
}
