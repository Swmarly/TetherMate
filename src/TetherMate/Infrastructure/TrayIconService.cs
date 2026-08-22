using System.ComponentModel;
using System.Drawing;
using Forms = System.Windows.Forms;
using TetherMate.Presentation;

namespace TetherMate.Infrastructure;

public sealed class TrayIconService : IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _connectionItem;
    private bool _shownBackgroundHint;

    public TrayIconService(
        MainViewModel viewModel,
        Action showWindow,
        Func<Task> exitAsync)
    {
        _viewModel = viewModel;
        var menu = new Forms.ContextMenuStrip();
        var showItem = new Forms.ToolStripMenuItem("Open TetherMate")
        {
            Font = new Font(menu.Font, FontStyle.Bold),
        };
        showItem.Click += (_, _) => showWindow();

        _connectionItem = new Forms.ToolStripMenuItem();
        _connectionItem.Click += (_, _) =>
        {
            if (_viewModel.ToggleConnectionCommand.CanExecute(null))
            {
                _viewModel.ToggleConnectionCommand.Execute(null);
            }
        };

        var diagnoseItem = new Forms.ToolStripMenuItem("Run diagnostics");
        diagnoseItem.Click += (_, _) =>
        {
            showWindow();
            if (_viewModel.DiagnoseCommand.CanExecute(null))
            {
                _viewModel.DiagnoseCommand.Execute(null);
            }
        };

        var exitItem = new Forms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => _ = exitAsync();

        menu.Items.Add(showItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_connectionItem);
        menu.Items.Add(diagnoseItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        var icon = Environment.ProcessPath is { } processPath
            ? Icon.ExtractAssociatedIcon(processPath)
            : null;
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "TetherMate",
            Icon = icon ?? SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => showWindow();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateMenu();
    }

    public void ShowBackgroundHint()
    {
        if (_shownBackgroundHint)
        {
            return;
        }

        _shownBackgroundHint = true;
        _notifyIcon.ShowBalloonTip(
            2500,
            "TetherMate is still running",
            "Use the tray icon to reopen it or turn off the wired link.",
            Forms.ToolTipIcon.Info);
    }

    public void Hide() => _notifyIcon.Visible = false;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(MainViewModel.IsSessionActive) or
            nameof(MainViewModel.ConnectionActionText) or
            nameof(MainViewModel.IsBusy) or
            nameof(MainViewModel.SelectedDevice) or
            nameof(MainViewModel.IsConnected))
        {
            UpdateMenu();
        }
    }

    private void UpdateMenu()
    {
        _connectionItem.Text = _viewModel.ConnectionActionText;
        _connectionItem.Enabled = _viewModel.ToggleConnectionCommand.CanExecute(null);
        _notifyIcon.Text = _viewModel.IsConnected
            ? "TetherMate — Connected over USB"
            : "TetherMate";
    }

    public void Dispose()
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
