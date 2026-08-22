using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using TetherMate.Core.Models;
using TetherMate.Core.Services;
using TetherMate.Infrastructure;

namespace TetherMate.Presentation;

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly BinaryManager _binaryManager;
    private readonly SettingsService _settingsService;
    private readonly FileLogger _fileLogger;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly AsyncRelayCommand _toggleConnectionCommand;
    private readonly AsyncRelayCommand _restartCommand;
    private readonly AsyncRelayCommand _refreshCommand;
    private readonly AsyncRelayCommand _diagnoseCommand;
    private readonly AsyncRelayCommand _repairClientCommand;

    private AppSettings _settings;
    private RuntimeBundle? _runtime;
    private AdbService? _adbService;
    private GnirehtetSession? _session;
    private Task? _monitorTask;
    private AndroidDevice? _selectedDevice;
    private bool _adbAvailable;
    private bool _isBusy;
    private bool _manualPause;
    private bool _isDiagnosticsVisible;
    private bool _isRefreshingSelection;
    private bool _initialized;
    private bool _disposed;
    private int _lastDeviceCount = -1;
    private int _autoFailureCount;
    private DateTimeOffset _nextAutoAttempt = DateTimeOffset.MinValue;
    private string _statusTitle = "Starting TetherMate";
    private string _statusDetail = "Preparing the bundled USB networking tools…";
    private string _statusTone = "Accent";
    private string _adbStepDetail = "Checking";
    private string _adbStepTone = "Neutral";
    private string _deviceStepDetail = "Waiting";
    private string _deviceStepTone = "Neutral";
    private string _tunnelStepDetail = "Off";
    private string _tunnelStepTone = "Neutral";
    private string _adbVersion = "Checking…";
    private string _diagnosticsSummary = "";

    public MainViewModel(
        Dispatcher dispatcher,
        BinaryManager binaryManager,
        SettingsService settingsService,
        FileLogger fileLogger)
    {
        _dispatcher = dispatcher;
        _binaryManager = binaryManager;
        _settingsService = settingsService;
        _fileLogger = fileLogger;
        _settings = settingsService.Load();

        _toggleConnectionCommand = new AsyncRelayCommand(
            ToggleConnectionAsync,
            CanToggleConnection,
            HandleCommandError);
        _restartCommand = new AsyncRelayCommand(
            RestartConnectionAsync,
            () => SelectedDevice?.IsReady == true && !_isBusy,
            HandleCommandError);
        _refreshCommand = new AsyncRelayCommand(
            () => RefreshDevicesAsync(logChanges: true, _shutdown.Token),
            () => _initialized && !_isBusy,
            HandleCommandError);
        _diagnoseCommand = new AsyncRelayCommand(
            RunDiagnosticsAsync,
            () => _initialized && !_isBusy,
            HandleCommandError);
        _repairClientCommand = new AsyncRelayCommand(
            RepairClientAsync,
            () => SelectedDevice?.IsReady == true && !_isBusy,
            HandleCommandError);

        ClearActivityCommand = new RelayCommand(Activity.Clear);
        CopyDiagnosticsCommand = new RelayCommand(CopyDiagnostics, () => Diagnostics.Count > 0);
        OpenLogsCommand = new RelayCommand(() => OpenPath(_fileLogger.LogDirectory));
        OpenSetupGuideCommand = new RelayCommand(() => OpenPath(
            "https://github.com/Swmarly/TetherMate#setup"));
        OpenNoticesCommand = new RelayCommand(OpenThirdPartyNotices);
    }

    public ObservableCollection<AndroidDevice> Devices { get; } = [];

    public ObservableCollection<ActivityEntry> Activity { get; } = [];

    public ObservableCollection<DiagnosticItem> Diagnostics { get; } = [];

    public AsyncRelayCommand ToggleConnectionCommand => _toggleConnectionCommand;

    public AsyncRelayCommand RestartCommand => _restartCommand;

    public AsyncRelayCommand RefreshCommand => _refreshCommand;

    public AsyncRelayCommand DiagnoseCommand => _diagnoseCommand;

    public AsyncRelayCommand RepairClientCommand => _repairClientCommand;

    public RelayCommand ClearActivityCommand { get; }

    public RelayCommand CopyDiagnosticsCommand { get; }

    public RelayCommand OpenLogsCommand { get; }

    public RelayCommand OpenSetupGuideCommand { get; }

    public RelayCommand OpenNoticesCommand { get; }

    public string AppVersion { get; } = GetAppVersion();

    public AndroidDevice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            var serialChanged = !string.Equals(
                _selectedDevice?.Serial,
                value?.Serial,
                StringComparison.Ordinal);

            if (!SetProperty(ref _selectedDevice, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedDeviceName));
            OnPropertyChanged(nameof(SelectedDeviceSerial));
            RaiseCommandStates();
            UpdateVisualState();

            if (serialChanged && !_isRefreshingSelection)
            {
                _manualPause = false;
                _settings = _settings with { PreferredSerial = value?.Serial };
                SaveSettings();
                _ = ChangeTargetSafelyAsync();
            }
        }
    }

    public string SelectedDeviceName => SelectedDevice?.FriendlyName ?? "No headset detected";

    public string SelectedDeviceSerial => SelectedDevice?.Serial ?? "Connect a USB data cable";

    public string DeviceCountLabel => Devices.Count switch
    {
        0 => "No ADB devices",
        1 => "1 ADB device",
        _ => $"{Devices.Count} ADB devices",
    };

    public bool AutoConnect
    {
        get => _settings.AutoConnect;
        set
        {
            if (_settings.AutoConnect == value)
            {
                return;
            }

            _settings = _settings with { AutoConnect = value };
            OnPropertyChanged();
            SaveSettings();
            if (value)
            {
                _manualPause = false;
                _autoFailureCount = 0;
                _nextAutoAttempt = DateTimeOffset.MinValue;
                _ = EvaluateSafelyAsync();
            }
        }
    }

    public bool CloseToTray
    {
        get => _settings.CloseToTray;
        set
        {
            if (_settings.CloseToTray == value)
            {
                return;
            }

            _settings = _settings with { CloseToTray = value };
            OnPropertyChanged();
            SaveSettings();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool IsDiagnosticsVisible
    {
        get => _isDiagnosticsVisible;
        set => SetProperty(ref _isDiagnosticsVisible, value);
    }

    public bool IsSessionActive => _session?.Snapshot.IsRelayRunning == true;

    public bool IsConnected => _session?.Snapshot.IsClientConnected == true;

    public string ConnectionActionText => IsSessionActive ? "Disconnect" : "Connect over USB";

    public string StatusTitle
    {
        get => _statusTitle;
        private set => SetProperty(ref _statusTitle, value);
    }

    public string StatusDetail
    {
        get => _statusDetail;
        private set => SetProperty(ref _statusDetail, value);
    }

    public string StatusTone
    {
        get => _statusTone;
        private set => SetProperty(ref _statusTone, value);
    }

    public string AdbStepDetail
    {
        get => _adbStepDetail;
        private set => SetProperty(ref _adbStepDetail, value);
    }

    public string AdbStepTone
    {
        get => _adbStepTone;
        private set => SetProperty(ref _adbStepTone, value);
    }

    public string DeviceStepDetail
    {
        get => _deviceStepDetail;
        private set => SetProperty(ref _deviceStepDetail, value);
    }

    public string DeviceStepTone
    {
        get => _deviceStepTone;
        private set => SetProperty(ref _deviceStepTone, value);
    }

    public string TunnelStepDetail
    {
        get => _tunnelStepDetail;
        private set => SetProperty(ref _tunnelStepDetail, value);
    }

    public string TunnelStepTone
    {
        get => _tunnelStepTone;
        private set => SetProperty(ref _tunnelStepTone, value);
    }

    public string AdbVersion
    {
        get => _adbVersion;
        private set => SetProperty(ref _adbVersion, value);
    }

    public string DiagnosticsSummary
    {
        get => _diagnosticsSummary;
        private set => SetProperty(ref _diagnosticsSummary, value);
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        Log("Starting TetherMate.");
        try
        {
            _runtime = await _binaryManager.EnsureRuntimeAsync(Log, _shutdown.Token);
            var processRunner = new ProcessRunner();
            _adbService = new AdbService(_runtime.AdbPath, processRunner, Log);
            _session = new GnirehtetSession(_runtime, _adbService, processRunner, Log);
            _session.StateChanged += OnSessionStateChanged;

            var adbResult = await _adbService.EnsureServerAsync(_shutdown.Token);
            _adbAvailable = adbResult.Succeeded;
            if (!adbResult.Succeeded)
            {
                Log($"ADB is unavailable: {adbResult.Message}");
            }

            AdbVersion = await _adbService.GetVersionAsync(_shutdown.Token);
            _initialized = true;
            RaiseCommandStates();
            await RefreshDevicesAsync(logChanges: true, _shutdown.Token);
            _monitorTask = MonitorLoopAsync(_shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Log($"Startup failed: {exception.Message}");
            SetStatus("TetherMate could not start", exception.Message, "Error");
        }
    }

    public async Task ShutdownAsync()
    {
        if (_disposed)
        {
            return;
        }

        _shutdown.Cancel();
        if (_monitorTask is not null)
        {
            try
            {
                await _monitorTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_session is not null)
        {
            _session.StateChanged -= OnSessionStateChanged;
            await _session.DisposeAsync();
        }

        _disposed = true;
        Log("TetherMate stopped.");
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2.5));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            await RefreshDevicesAsync(logChanges: false, cancellationToken);
        }
    }

    private async Task RefreshDevicesAsync(bool logChanges, CancellationToken cancellationToken)
    {
        if (_adbService is null || !_initialized)
        {
            return;
        }

        if (!await _refreshGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            if (!_adbAvailable)
            {
                var server = await _adbService.EnsureServerAsync(cancellationToken);
                _adbAvailable = server.Succeeded;
                if (!_adbAvailable)
                {
                    SetStatus("ADB is unavailable", server.Message, "Error");
                    UpdateVisualState();
                    return;
                }
            }

            var devices = await _adbService.GetDevicesAsync(cancellationToken);
            _adbAvailable = true;
            ApplyDeviceSnapshot(devices);

            if (logChanges || _lastDeviceCount != devices.Count)
            {
                Log(devices.Count == 0
                    ? "No ADB devices detected."
                    : $"Detected {devices.Count} ADB device(s).");
                _lastDeviceCount = devices.Count;
            }

            await EvaluateStateAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _adbAvailable = false;
            Log(exception.Message);
            SetStatus("ADB scan failed", exception.Message, "Error");
            UpdateVisualState();
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void ApplyDeviceSnapshot(IReadOnlyList<AndroidDevice> devices)
    {
        _isRefreshingSelection = true;
        try
        {
            var wantedSerial = SelectedDevice?.Serial ?? _settings.PreferredSerial;
            Devices.Clear();
            foreach (var device in devices)
            {
                Devices.Add(device);
            }

            SelectedDevice = Devices.FirstOrDefault(device => device.Serial == wantedSerial)
                ?? Devices.FirstOrDefault(IsLikelyQuest)
                ?? Devices.FirstOrDefault();
            OnPropertyChanged(nameof(DeviceCountLabel));
        }
        finally
        {
            _isRefreshingSelection = false;
        }
    }

    private async Task EvaluateStateAsync(CancellationToken cancellationToken)
    {
        if (_session is null)
        {
            return;
        }

        var selected = SelectedDevice;
        var snapshot = _session.Snapshot;
        if (selected?.IsReady != true)
        {
            if (snapshot.IsRelayRunning || snapshot.ActiveSerial is not null)
            {
                await _session.StopAsync(CancellationToken.None);
            }

            UpdateVisualState();
            return;
        }

        if (snapshot.ActiveSerial is not null && snapshot.ActiveSerial != selected.Serial)
        {
            await _session.StopAsync(CancellationToken.None);
            snapshot = _session.Snapshot;
        }

        UpdateVisualState();

        if (!snapshot.IsRelayRunning &&
            AutoConnect &&
            !_manualPause &&
            DateTimeOffset.Now >= _nextAutoAttempt)
        {
            await ConnectCoreAsync(isAutomatic: true, cancellationToken);
        }
    }

    private async Task ToggleConnectionAsync()
    {
        if (_session?.Snapshot.IsRelayRunning == true)
        {
            await DisconnectCoreAsync(manual: true, _shutdown.Token);
        }
        else
        {
            _manualPause = false;
            _autoFailureCount = 0;
            _nextAutoAttempt = DateTimeOffset.MinValue;
            await ConnectCoreAsync(isAutomatic: false, _shutdown.Token);
        }
    }

    private async Task ConnectCoreAsync(bool isAutomatic, CancellationToken cancellationToken)
    {
        if (_session is null || SelectedDevice?.IsReady != true)
        {
            UpdateVisualState();
            return;
        }

        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            if (_session.Snapshot.IsRelayRunning)
            {
                return;
            }

            IsBusy = true;
            SetStatus(
                isAutomatic ? "Connecting automatically" : "Connecting over USB",
                "Starting the PC relay and preparing the headset…",
                "Accent");

            var result = await _session.StartAsync(SelectedDevice.Serial, cancellationToken);
            if (!result.Succeeded)
            {
                Log(result.Message);
                ScheduleAutoRetry();
                SetStatus(
                    "Could not start the wired link",
                    isAutomatic
                        ? $"{result.Message} TetherMate will retry automatically."
                        : result.Message,
                    "Error");
            }
            else
            {
                _autoFailureCount = 0;
                _nextAutoAttempt = DateTimeOffset.MinValue;
                UpdateVisualState();
            }
        }
        finally
        {
            IsBusy = false;
            _connectionGate.Release();
            RaiseConnectionProperties();
        }
    }

    private async Task DisconnectCoreAsync(bool manual, CancellationToken cancellationToken)
    {
        if (_session is null)
        {
            return;
        }

        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            IsBusy = true;
            if (manual)
            {
                _manualPause = true;
            }

            SetStatus("Disconnecting", "Stopping the headset VPN and PC relay…", "Accent");
            await _session.StopAsync(CancellationToken.None);
            UpdateVisualState();
        }
        finally
        {
            IsBusy = false;
            _connectionGate.Release();
            RaiseConnectionProperties();
        }
    }

    private async Task RestartConnectionAsync()
    {
        if (_session is null || SelectedDevice?.IsReady != true)
        {
            return;
        }

        await _connectionGate.WaitAsync(_shutdown.Token);
        try
        {
            IsBusy = true;
            _manualPause = false;
            SetStatus("Repairing the wired link", "Rebuilding the relay and USB tunnel…", "Accent");
            var result = await _session.RestartAsync(SelectedDevice.Serial, _shutdown.Token);
            if (!result.Succeeded)
            {
                Log(result.Message);
                SetStatus("Repair failed", result.Message, "Error");
            }
            else
            {
                UpdateVisualState();
            }
        }
        finally
        {
            IsBusy = false;
            _connectionGate.Release();
            RaiseConnectionProperties();
        }
    }

    private async Task ChangeTargetSafelyAsync()
    {
        try
        {
            if (_session is not null &&
                _session.Snapshot.ActiveSerial is { } activeSerial &&
                activeSerial != SelectedDevice?.Serial)
            {
                await _session.StopAsync(CancellationToken.None);
            }

            await EvaluateStateAsync(_shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            HandleCommandError(exception);
        }
    }

    private async Task EvaluateSafelyAsync()
    {
        try
        {
            await EvaluateStateAsync(_shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            HandleCommandError(exception);
        }
    }

    private async Task RunDiagnosticsAsync()
    {
        if (_adbService is null || _session is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            IsDiagnosticsVisible = true;
            Diagnostics.Clear();
            Diagnostics.Add(new DiagnosticItem(
                "Bundled runtime",
                _binaryManager.IsRuntimeComplete() ? "Files verified" : "One or more files are missing",
                _binaryManager.IsRuntimeComplete() ? "Success" : "Error"));

            var server = await _adbService.EnsureServerAsync(_shutdown.Token);
            Diagnostics.Add(new DiagnosticItem(
                "Android Debug Bridge",
                server.Succeeded ? AdbVersion : server.Message,
                server.Succeeded ? "Success" : "Error"));

            await RefreshDevicesAsync(logChanges: false, _shutdown.Token);
            var device = SelectedDevice;
            Diagnostics.Add(device is null
                ? new DiagnosticItem("USB headset", "No ADB device detected", "Error")
                : new DiagnosticItem("USB headset", device.DisplayName, "Success"));

            Diagnostics.Add(device is null
                ? new DiagnosticItem("USB authorization", "Waiting for a headset", "Neutral")
                : device.State == AdbDeviceState.Unauthorized
                    ? new DiagnosticItem("USB authorization", "Allow USB debugging inside the headset", "Warning")
                    : device.IsReady
                        ? new DiagnosticItem("USB authorization", "Authorized and responsive", "Success")
                        : new DiagnosticItem("USB authorization", $"Device state: {device.RawState}", "Error"));

            var snapshot = _session.Snapshot;
            Diagnostics.Add(new DiagnosticItem(
                "PC relay",
                snapshot.IsRelayRunning ? "Listening on TCP 31416" : "Not running",
                snapshot.IsRelayRunning ? "Success" : "Neutral"));

            var hasTunnel = device?.IsReady == true &&
                            await _adbService.HasGnirehtetTunnelAsync(device.Serial, _shutdown.Token);
            Diagnostics.Add(new DiagnosticItem(
                "ADB reverse tunnel",
                hasTunnel ? "localabstract:gnirehtet → tcp:31416" : "Not active",
                hasTunnel ? "Success" : "Neutral"));

            Diagnostics.Add(new DiagnosticItem(
                "Headset VPN client",
                snapshot.IsClientConnected
                    ? "Connected to the PC relay"
                    : snapshot.IsRelayRunning
                        ? "Waiting — check the VPN prompt in the headset"
                        : "Not active",
                snapshot.IsClientConnected ? "Success" : snapshot.IsRelayRunning ? "Warning" : "Neutral"));

            var successes = Diagnostics.Count(item => item.Tone == "Success");
            DiagnosticsSummary = $"{successes} of {Diagnostics.Count} checks are active";
            ((RelayCommand)CopyDiagnosticsCommand).RaiseCanExecuteChanged();
            Log("Diagnostics completed.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RepairClientAsync()
    {
        if (_session is null || SelectedDevice?.IsReady != true)
        {
            return;
        }

        var serial = SelectedDevice.Serial;
        await _connectionGate.WaitAsync(_shutdown.Token);
        try
        {
            IsBusy = true;
            _manualPause = false;
            SetStatus(
                "Repairing the headset client",
                "Reinstalling the VPN helper, then rebuilding the USB link…",
                "Accent");

            var reinstall = await _session.ReinstallClientAsync(serial, _shutdown.Token);
            if (!reinstall.Succeeded)
            {
                Log(reinstall.Message);
                SetStatus("Client repair failed", reinstall.Message, "Error");
                return;
            }

            Log(reinstall.Message);
            var start = await _session.StartAsync(serial, _shutdown.Token);
            if (!start.Succeeded)
            {
                Log(start.Message);
                SetStatus("USB link still needs attention", start.Message, "Error");
                return;
            }

            _autoFailureCount = 0;
            _nextAutoAttempt = DateTimeOffset.MinValue;
            UpdateVisualState();
        }
        finally
        {
            IsBusy = false;
            _connectionGate.Release();
            RaiseConnectionProperties();
        }
    }

    private void UpdateVisualState()
    {
        var device = SelectedDevice;
        var snapshot = _session?.Snapshot ?? SessionSnapshot.Stopped;

        AdbStepDetail = _adbAvailable ? "Available" : "Unavailable";
        AdbStepTone = _adbAvailable ? "Success" : "Error";

        if (device is null)
        {
            DeviceStepDetail = "Not detected";
            DeviceStepTone = "Neutral";
        }
        else if (device.State == AdbDeviceState.Unauthorized)
        {
            DeviceStepDetail = "Authorization needed";
            DeviceStepTone = "Warning";
        }
        else if (device.IsReady)
        {
            DeviceStepDetail = "Ready";
            DeviceStepTone = "Success";
        }
        else
        {
            DeviceStepDetail = device.RawState;
            DeviceStepTone = "Error";
        }

        if (snapshot.IsClientConnected)
        {
            TunnelStepDetail = "Traffic flowing";
            TunnelStepTone = "Success";
            SetStatus(
                "Connected over USB",
                "Headset traffic is flowing through this PC. You can open Virtual Desktop now.",
                "Success");
        }
        else if (snapshot.IsRelayRunning)
        {
            TunnelStepDetail = "Waiting for VPN";
            TunnelStepTone = "Warning";
            SetStatus(
                "Finish setup in the headset",
                "Accept the VPN connection prompt. The link will turn green when the client connects.",
                "Warning");
        }
        else if (!string.IsNullOrWhiteSpace(snapshot.LastError))
        {
            TunnelStepDetail = "Needs attention";
            TunnelStepTone = "Error";
            SetStatus("The wired link stopped", snapshot.LastError, "Error");
        }
        else if (!_adbAvailable)
        {
            TunnelStepDetail = "Off";
            TunnelStepTone = "Neutral";
            SetStatus(
                "ADB is unavailable",
                "TetherMate could not start the bundled Android Debug Bridge.",
                "Error");
        }
        else if (device is null)
        {
            TunnelStepDetail = "Off";
            TunnelStepTone = "Neutral";
            SetStatus(
                "Connect your headset",
                "Use a USB 3 data cable, then put on the headset and allow USB debugging.",
                "Neutral");
        }
        else if (device.State == AdbDeviceState.Unauthorized)
        {
            TunnelStepDetail = "Off";
            TunnelStepTone = "Neutral";
            SetStatus(
                "Allow USB debugging",
                "Put on the headset and accept the USB debugging prompt. Choose “Always allow” if available.",
                "Warning");
        }
        else if (!device.IsReady)
        {
            TunnelStepDetail = "Off";
            TunnelStepTone = "Neutral";
            SetStatus(
                "Headset is not ready",
                $"ADB reports the device as “{device.RawState}”. Reconnect the cable or wake the headset.",
                "Error");
        }
        else if (_manualPause)
        {
            TunnelStepDetail = "Paused";
            TunnelStepTone = "Neutral";
            SetStatus(
                "Wired link paused",
                "The headset is ready. Select Connect over USB when you want to use the cable.",
                "Neutral");
        }
        else
        {
            TunnelStepDetail = "Ready";
            TunnelStepTone = "Accent";
            SetStatus(
                "Ready to connect",
                AutoConnect
                    ? "TetherMate will start the wired link automatically."
                    : "Select Connect over USB to route headset traffic through the cable.",
                "Accent");
        }

        RaiseConnectionProperties();
    }

    private void SetStatus(string title, string detail, string tone)
    {
        StatusTitle = title;
        StatusDetail = detail;
        StatusTone = tone;
    }

    private void ScheduleAutoRetry()
    {
        _autoFailureCount++;
        var seconds = Math.Min(30, 3 * Math.Pow(2, Math.Min(3, _autoFailureCount - 1)));
        _nextAutoAttempt = DateTimeOffset.Now.AddSeconds(seconds);
    }

    private bool CanToggleConnection()
    {
        if (_isBusy || !_initialized || _session is null)
        {
            return false;
        }

        return _session.Snapshot.IsRelayRunning || SelectedDevice?.IsReady == true;
    }

    private void OnSessionStateChanged(object? sender, EventArgs eventArgs)
    {
        if (_dispatcher.CheckAccess())
        {
            UpdateVisualState();
        }
        else
        {
            _dispatcher.BeginInvoke(UpdateVisualState);
        }
    }

    private void Log(string message)
    {
        _fileLogger.Write(message);
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => AddActivity(message));
            return;
        }

        AddActivity(message);
    }

    private void AddActivity(string message)
    {
        Activity.Insert(0, new ActivityEntry(DateTimeOffset.Now, message));
        while (Activity.Count > 300)
        {
            Activity.RemoveAt(Activity.Count - 1);
        }
    }

    private void HandleCommandError(Exception exception)
    {
        if (exception is OperationCanceledException && _shutdown.IsCancellationRequested)
        {
            return;
        }

        Log($"Operation failed: {exception.Message}");
        SetStatus("Operation failed", exception.Message, "Error");
    }

    private void RaiseCommandStates()
    {
        _toggleConnectionCommand.RaiseCanExecuteChanged();
        _restartCommand.RaiseCanExecuteChanged();
        _refreshCommand.RaiseCanExecuteChanged();
        _diagnoseCommand.RaiseCanExecuteChanged();
        _repairClientCommand.RaiseCanExecuteChanged();
    }

    private void RaiseConnectionProperties()
    {
        OnPropertyChanged(nameof(IsSessionActive));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(ConnectionActionText));
        RaiseCommandStates();
    }

    private void SaveSettings()
    {
        try
        {
            _settingsService.Save(_settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log($"Could not save settings: {exception.Message}");
        }
    }

    private void CopyDiagnostics()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"TetherMate {AppVersion}");
        builder.AppendLine($"Status: {StatusTitle}");
        builder.AppendLine($"Device: {SelectedDevice?.DisplayName ?? "None"}");
        foreach (var check in Diagnostics)
        {
            builder.AppendLine($"{check.Name}: {check.Detail}");
        }

        try
        {
            System.Windows.Clipboard.SetText(builder.ToString());
            Log("Diagnostics copied to the clipboard.");
        }
        catch (System.Runtime.InteropServices.ExternalException exception)
        {
            Log($"Could not access the clipboard: {exception.Message}");
        }
    }

    private void OpenThirdPartyNotices()
    {
        var noticePath = _runtime is null
            ? Path.Combine(AppPaths.RuntimeDirectory, "THIRD-PARTY-NOTICES.txt")
            : Path.Combine(_runtime.Directory, "THIRD-PARTY-NOTICES.txt");
        OpenPath(noticePath);
    }

    private static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            System.Windows.MessageBox.Show(
                exception.Message,
                "TetherMate",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private static bool IsLikelyQuest(AndroidDevice device)
    {
        var identity = $"{device.Manufacturer} {device.Model} {device.Product}";
        return identity.Contains("quest", StringComparison.OrdinalIgnoreCase) ||
               identity.Contains("oculus", StringComparison.OrdinalIgnoreCase) ||
               identity.Contains("meta", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetAppVersion()
    {
        var assembly = Assembly.GetEntryAssembly();
        var informational = assembly?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return (informational ?? assembly?.GetName().Version?.ToString() ?? "dev")
            .Split('+')[0];
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync();
        _shutdown.Dispose();
        _refreshGate.Dispose();
        _connectionGate.Dispose();
    }
}
