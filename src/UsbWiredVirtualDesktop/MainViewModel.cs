using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace UsbWiredVirtualDesktop;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ObservableCollection<DeviceInfo> _devices = new();
    private readonly ObservableCollection<LogEntry> _logEntries = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly RelayCommand _startCommand;
    private readonly RelayCommand _stopCommand;
    private readonly RelayCommand _restartCommand;

    private DeviceInfo? _selectedDevice;
    private string _adbStatus = "Checking...";
    private string _deviceStatus = "Waiting for device";
    private string _gnirehtetStatus = "Stopped";
    private bool _stopOnExit = true;
    private DateTimeOffset? _readySince;
    private string? _activeSerial;
    private AdbService? _adbService;
    private GnirehtetManager? _gnirehtetManager;
    private BinaryManager? _binaryManager;

    public MainViewModel()
    {
        _startCommand = new RelayCommand(() => TriggerManualStart(), () => SelectedDevice is not null);
        _stopCommand = new RelayCommand(() => TriggerManualStop(), () => SelectedDevice is not null);
        _restartCommand = new RelayCommand(() => TriggerManualRestart(), () => SelectedDevice is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DeviceInfo> Devices => _devices;
    public ObservableCollection<LogEntry> LogEntries => _logEntries;

    public DeviceInfo? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                _startCommand.RaiseCanExecuteChanged();
                _stopCommand.RaiseCanExecuteChanged();
                _restartCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string AdbStatus
    {
        get => _adbStatus;
        private set => SetProperty(ref _adbStatus, value);
    }

    public string DeviceStatus
    {
        get => _deviceStatus;
        private set => SetProperty(ref _deviceStatus, value);
    }

    public string GnirehtetStatus
    {
        get => _gnirehtetStatus;
        private set => SetProperty(ref _gnirehtetStatus, value);
    }

    public bool StopOnExit
    {
        get => _stopOnExit;
        set => SetProperty(ref _stopOnExit, value);
    }

    public RelayCommand StartCommand => _startCommand;
    public RelayCommand StopCommand => _stopCommand;
    public RelayCommand RestartCommand => _restartCommand;

    public async Task InitializeAsync()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var binDir = Path.Combine(appData, "UsbWiredVirtualDesktop", "bin");
        _binaryManager = new BinaryManager(binDir);
        await _binaryManager.EnsureExtractedAsync(AppendLog);

        _adbService = new AdbService(_binaryManager.AdbPath, AppendLog);
        _gnirehtetManager = new GnirehtetManager(_binaryManager.GnirehtetPath, AppendLog);
        await _gnirehtetManager.CleanupOrphansAsync();

        _ = Task.Run(() => MonitorLoopAsync(_cts.Token));
    }

    public void RequestShutdown()
    {
        _cts.Cancel();
        if (StopOnExit && _gnirehtetManager is not null && !string.IsNullOrWhiteSpace(_activeSerial))
        {
            _ = _gnirehtetManager.StopAsync(_activeSerial);
        }
    }

    private async Task MonitorLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await RefreshDevicesAsync();
                await EvaluateStateAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"Monitor error: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), token);
        }
    }

    private async Task RefreshDevicesAsync()
    {
        if (_adbService is null)
        {
            return;
        }

        var adbOk = await _adbService.EnsureServerAsync();
        AdbStatus = adbOk ? "Available" : "Unavailable";
        if (!adbOk)
        {
            DeviceStatus = "ADB unavailable";
            Application.Current.Dispatcher.Invoke(() => _devices.Clear());
            return;
        }

        var devices = await _adbService.GetDevicesAsync();
        foreach (var device in devices)
        {
            await _adbService.ProbeDeviceAsync(device);
        }

        Application.Current.Dispatcher.Invoke(() =>
        {
            _devices.Clear();
            foreach (var device in devices)
            {
                _devices.Add(device);
            }

            if (SelectedDevice is null || !_devices.Any(d => d.Serial == SelectedDevice.Serial))
            {
                SelectedDevice = _devices.FirstOrDefault();
            }
        });
    }

    private async Task EvaluateStateAsync()
    {
        var selected = SelectedDevice;
        if (selected is null)
        {
            DeviceStatus = "No device selected";
            GnirehtetStatus = "Stopped";
            _readySince = null;
            return;
        }

        DeviceStatus = selected.IsReady
            ? "Ready"
            : selected.State.Equals("unauthorized", StringComparison.OrdinalIgnoreCase)
                ? "Unauthorized"
                : selected.State.Equals("offline", StringComparison.OrdinalIgnoreCase)
                    ? "Offline"
                    : "Not ready";

        if (!selected.IsReady)
        {
            _readySince = null;
        }
        else if (_readySince is null)
        {
            _readySince = DateTimeOffset.Now;
        }

        if (_gnirehtetManager is null)
        {
            return;
        }

        if (!selected.IsReady)
        {
            if (_gnirehtetManager.IsRunning)
            {
                await _gnirehtetManager.StopAsync(selected.Serial);
                GnirehtetStatus = "Stopped";
            }

            return;
        }

        if (_activeSerial != selected.Serial && _gnirehtetManager.IsRunning)
        {
            await _gnirehtetManager.StopAsync(_activeSerial ?? selected.Serial);
            GnirehtetStatus = "Stopped";
        }

        if (_readySince.HasValue && DateTimeOffset.Now - _readySince.Value > TimeSpan.FromSeconds(3))
        {
            if (!_gnirehtetManager.IsRunning)
            {
                await _gnirehtetManager.StartAsync(selected.Serial);
                _activeSerial = selected.Serial;
                GnirehtetStatus = "Running";
            }
        }

        if (_gnirehtetManager.IsRunning)
        {
            GnirehtetStatus = "Running";
        }
    }

    private void TriggerManualStart()
    {
        if (SelectedDevice is null || _gnirehtetManager is null)
        {
            return;
        }

        _readySince = DateTimeOffset.Now.Subtract(TimeSpan.FromSeconds(4));
        _ = _gnirehtetManager.StartAsync(SelectedDevice.Serial);
        GnirehtetStatus = "Running";
        _activeSerial = SelectedDevice.Serial;
    }

    private void TriggerManualStop()
    {
        if (SelectedDevice is null || _gnirehtetManager is null)
        {
            return;
        }

        _ = _gnirehtetManager.StopAsync(SelectedDevice.Serial);
        GnirehtetStatus = "Stopped";
    }

    private void TriggerManualRestart()
    {
        if (SelectedDevice is null || _gnirehtetManager is null)
        {
            return;
        }

        _ = _gnirehtetManager.RestartAsync(SelectedDevice.Serial);
        GnirehtetStatus = "Restarting";
        _activeSerial = SelectedDevice.Serial;
    }

    private void AppendLog(string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _logEntries.Insert(0, new LogEntry { Message = message });
            if (_logEntries.Count > 500)
            {
                _logEntries.RemoveAt(_logEntries.Count - 1);
            }
        });
    }

    private bool SetProperty<T>(ref T backingField, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(backingField, value))
        {
            return false;
        }

        backingField = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
