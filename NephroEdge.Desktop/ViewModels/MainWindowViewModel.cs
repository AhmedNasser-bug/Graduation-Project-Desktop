using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using NephroEdge.Desktop.Bootstrap;
using NephroEdge.Desktop.Infrastructure;
using Services_Unit.Core.Interfaces;
using Services_Unit.SyncBridge;

namespace NephroEdge.Desktop.ViewModels;

/// <summary>
/// Root ViewModel for the NephroEdge Doctor Workstation shell.
/// Coordinates left-rail workspace navigation, the IPSG.1 Sticky 2-ID Patient Banner,
/// IEC 60601-1-8 alarm flash cadence (1.43 Hz High / 0.71 Hz Medium), and Steady Cyan sync diagnostics.
/// </summary>
public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Func<LocalSyncHttpHost?> _getSyncHost;
    private readonly Func<LocalSyncBindMode, Task> _restartWithBindModeAsync;
    private readonly Action? _disposeOwnedResources;
    private readonly DispatcherTimer _alarmCadenceTimer;

    private WorkspaceTab _activeTab = WorkspaceTab.ShiftGrid;
    private bool _highFlashOn = true;
    private bool _mediumFlashOn = true;
    private int _alarmTickCount;
    private bool _isSyncHostListening;
    private string _syncIndicatorBadgeText = "◈ Bedside Sync · Standby";
    private string _syncIndicatorDetailText = "Offline Queue Ready";
    private int _disposed;

    public StickyPatientBannerViewModel BannerVm { get; }
    public ShiftGridViewModel ShiftGridVm { get; }
    public AnalyticsViewModel AnalyticsVm { get; }
    public SafetyRxWorkbenchViewModel SafetyRxVm { get; }
    public SyncMonitorViewModel SyncMonitorVm { get; }

    public WorkspaceTab ActiveTab
    {
        get => _activeTab;
        set
        {
            if (SetProperty(ref _activeTab, value))
            {
                OnPropertyChanged(nameof(IsShiftGridActive));
                OnPropertyChanged(nameof(IsAnalyticsActive));
                OnPropertyChanged(nameof(IsSafetyRxActive));
                OnPropertyChanged(nameof(IsSyncMonitorActive));
                RefreshActiveWorkspace(value);
            }
        }
    }

    public bool IsShiftGridActive => ActiveTab == WorkspaceTab.ShiftGrid;
    public bool IsAnalyticsActive => ActiveTab == WorkspaceTab.PatientAnalytics;
    public bool IsSafetyRxActive => ActiveTab == WorkspaceTab.SafetyRxWorkbench;
    public bool IsSyncMonitorActive => ActiveTab == WorkspaceTab.SyncMonitor;

    public bool IsSyncHostListening
    {
        get => _isSyncHostListening;
        private set => SetProperty(ref _isSyncHostListening, value);
    }

    public string SyncIndicatorBadgeText
    {
        get => _syncIndicatorBadgeText;
        private set => SetProperty(ref _syncIndicatorBadgeText, value);
    }

    public string SyncIndicatorDetailText
    {
        get => _syncIndicatorDetailText;
        private set => SetProperty(ref _syncIndicatorDetailText, value);
    }

    public RelayCommand NavigateShiftGridCommand { get; }
    public RelayCommand NavigateAnalyticsCommand { get; }
    public RelayCommand NavigateSafetyRxCommand { get; }
    public RelayCommand NavigateSyncMonitorCommand { get; }
    public RelayCommand NavigateToTabCommand { get; }
    public RelayCommand RefreshActiveWorkspaceCommand { get; }

    public MainWindowViewModel(
        IServiceScopeFactory scopeFactory,
        Func<LocalSyncHttpHost?> getSyncHost,
        Func<LocalSyncBindMode, Task> restartWithBindModeAsync,
        Action? disposeOwnedResources = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _getSyncHost = getSyncHost ?? throw new ArgumentNullException(nameof(getSyncHost));
        _restartWithBindModeAsync = restartWithBindModeAsync ?? throw new ArgumentNullException(nameof(restartWithBindModeAsync));
        _disposeOwnedResources = disposeOwnedResources;

        BannerVm = new StickyPatientBannerViewModel(_scopeFactory, OnBannerPatientSelected);
        ShiftGridVm = new ShiftGridViewModel(_scopeFactory, OnChildSelectPatientAndNavigate, NavigateToSyncMonitorAndFocus);
        AnalyticsVm = new AnalyticsViewModel(_scopeFactory, OnPatientRecordUpdated);
        SafetyRxVm = new SafetyRxWorkbenchViewModel(_scopeFactory, OnPatientRecordUpdated);
        SyncMonitorVm = new SyncMonitorViewModel(
            _scopeFactory,
            _getSyncHost,
            RestartSyncHostWithShellRefreshAsync,
            OnSyncConflictResolved,
            OnOpenPatientChartFromSync);

        NavigateShiftGridCommand = new RelayCommand(() => SelectWorkspaceTab(WorkspaceTab.ShiftGrid));
        NavigateAnalyticsCommand = new RelayCommand(() => SelectWorkspaceTab(WorkspaceTab.PatientAnalytics));
        NavigateSafetyRxCommand = new RelayCommand(() => SelectWorkspaceTab(WorkspaceTab.SafetyRxWorkbench));
        NavigateSyncMonitorCommand = new RelayCommand(() => SelectWorkspaceTab(WorkspaceTab.SyncMonitor));
        NavigateToTabCommand = new RelayCommand(HandleNavigateParameter);
        RefreshActiveWorkspaceCommand = new RelayCommand(RefreshCurrentWorkspace);

        InitializeWorkspaceData();

        // IEC 60601-1-8: 350ms tick -> 700ms full High cycle (1.43 Hz) and 1400ms Medium cycle (0.71 Hz)
        _alarmCadenceTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(350),
            DispatcherPriority.Background,
            OnAlarmCadenceTick);
        _alarmCadenceTimer.Start();
    }

    public static MainWindowViewModel CreateFromServiceProvider(IServiceProvider sp, LocalSyncHttpHost? host = null)
    {
        ArgumentNullException.ThrowIfNull(sp);

        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        LocalSyncHttpHost? managedHost = host;
        bool ownsManagedHost = false;
        var hostLock = new SemaphoreSlim(1, 1);

        return new MainWindowViewModel(
            scopeFactory,
            getSyncHost: () => managedHost,
            restartWithBindModeAsync: async bindMode =>
            {
                await hostLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    LocalSyncHttpHost? previous = managedHost;
                    int port = previous?.Options.Port ?? ClinicWorkspaceBootstrap.DefaultSyncPort;
                    string sessionDate = string.IsNullOrWhiteSpace(previous?.Options.DefaultSessionDate)
                        ? ClinicWorkspaceBootstrap.DefaultShiftDate
                        : previous!.Options.DefaultSessionDate;
                    int shiftNumber = previous?.Options.DefaultShiftNumber ?? ClinicWorkspaceBootstrap.DefaultShiftNumber;

                    managedHost = null;
                    if (previous is not null)
                    {
                        await Task.Run(previous.Dispose).ConfigureAwait(false);
                    }

                    var nextOptions = new LocalSyncHostOptions(
                        Port: port,
                        BindMode: bindMode,
                        DefaultSessionDate: sessionDate,
                        DefaultShiftNumber: shiftNumber);

                    LocalSyncHttpHost nextHost = Services_Unit.DependencyInjection.ServicesUnitServiceExtensions
                        .CreateLocalSyncHttpHost(sp, nextOptions);
                    try
                    {
                        nextHost.Start();
                        managedHost = nextHost;
                        ownsManagedHost = true;
                    }
                    catch
                    {
                        nextHost.Dispose();
                        throw;
                    }
                }
                finally
                {
                    hostLock.Release();
                }
            },
            disposeOwnedResources: () =>
            {
                if (ownsManagedHost)
                {
                    try
                    {
                        managedHost?.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Already disposed.
                    }
                }

                managedHost = null;
                hostLock.Dispose();
            });
    }

    public void SelectWorkspaceTab(WorkspaceTab tab)
    {
        ActiveTab = tab;
    }

    public void RefreshCurrentWorkspace()
    {
        RefreshActiveWorkspace(ActiveTab);
    }

    public void NavigateToSyncMonitorAndFocus(string? patientOrHandshakeId)
    {
        NavigateToSyncMonitorAndFocus(patientId: null, handshakeOrPatientId: patientOrHandshakeId);
    }

    public void NavigateToSyncMonitorAndFocus(string? patientId, string? handshakeOrPatientId)
    {
        if (!string.IsNullOrWhiteSpace(patientId))
        {
            BannerVm.SelectPatientById(patientId, notifyListeners: false);
            string? effectiveId = BannerVm.SelectedPatientId ?? patientId;
            ShiftGridVm.SyncSelectionToPatient(effectiveId);
            AnalyticsVm.LoadForPatient(effectiveId);
            SafetyRxVm.LoadForPatient(effectiveId);
        }

        ActiveTab = WorkspaceTab.SyncMonitor;
        SyncMonitorVm.FocusDirective(handshakeOrPatientId ?? patientId);
    }

    public void RefreshShellSyncIndicator()
    {
        LocalSyncHttpHost? host = _getSyncHost();
        SyncHostDiagnosticsSnapshot? snapshot = host?.GetDiagnosticsSnapshot();

        if (snapshot is not null && snapshot.IsListening)
        {
            IsSyncHostListening = true;
            SyncIndicatorBadgeText = snapshot.BindMode == LocalSyncBindMode.ClinicLanBroadcast
                ? "◈ Bedside Sync · LAN Active"
                : "◈ Bedside Sync · ADB Ready";
            SyncIndicatorDetailText = snapshot.RequestsHandled == 1
                ? "1 sync request"
                : $"{snapshot.RequestsHandled} sync requests";
        }
        else
        {
            IsSyncHostListening = false;
            SyncIndicatorBadgeText = "◈ Bedside Sync · Standby";
            SyncIndicatorDetailText = "Offline Queue Ready";
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _alarmCadenceTimer.Stop();
        _alarmCadenceTimer.Tick -= OnAlarmCadenceTick;
        _disposeOwnedResources?.Invoke();
    }

    private void InitializeWorkspaceData()
    {
        string? preferredPatientId = null;
        using (var scope = _scopeFactory.CreateScope())
        {
            var sessionService = scope.ServiceProvider.GetRequiredService<ISessionService>();
            var activeBeds = sessionService.GetActiveShiftBoard(
                ClinicWorkspaceBootstrap.DefaultShiftDate,
                ClinicWorkspaceBootstrap.DefaultShiftNumber);
            preferredPatientId = activeBeds.FirstOrDefault()?.PatientId;
        }

        BannerVm.LoadPatients(preferredPatientId);
        ShiftGridVm.RefreshShiftBoard();
        BannerVm.UpdateShiftContext(ShiftGridVm.AllBedTiles);

        string? selectedPatientId = BannerVm.SelectedPatientId;
        ShiftGridVm.SyncSelectionToPatient(selectedPatientId);
        AnalyticsVm.LoadForPatient(selectedPatientId);
        SafetyRxVm.LoadForPatient(selectedPatientId);
        SyncMonitorVm.RefreshDiagnostics();
        RefreshShellSyncIndicator();
    }

    private void OnChildSelectPatientAndNavigate(string patientId, WorkspaceTab? targetTab)
    {
        if (!string.IsNullOrWhiteSpace(patientId))
        {
            BannerVm.SelectPatientById(patientId, notifyListeners: false);
            string? effectiveId = BannerVm.SelectedPatientId ?? patientId;
            ShiftGridVm.SyncSelectionToPatient(effectiveId);
            AnalyticsVm.LoadForPatient(effectiveId);
            SafetyRxVm.LoadForPatient(effectiveId);
        }

        if (targetTab.HasValue)
        {
            ActiveTab = targetTab.Value;
        }
    }

    private void OnBannerPatientSelected(string? patientId)
    {
        ShiftGridVm.SyncSelectionToPatient(patientId);
        AnalyticsVm.LoadForPatient(patientId);
        SafetyRxVm.LoadForPatient(patientId);
    }

    private void OnPatientRecordUpdated(string patientId)
    {
        BannerVm.LoadPatients(patientId);
        ShiftGridVm.RefreshShiftBoard();
        BannerVm.UpdateShiftContext(ShiftGridVm.AllBedTiles);

        string? activeId = BannerVm.SelectedPatientId ?? patientId;
        ShiftGridVm.SyncSelectionToPatient(activeId);
        AnalyticsVm.LoadForPatient(activeId);
        SafetyRxVm.LoadForPatient(activeId);
        SyncMonitorVm.RefreshDiagnostics();
        RefreshShellSyncIndicator();
    }

    private void OnSyncConflictResolved(string? patientId)
    {
        ShiftGridVm.RefreshShiftBoard();
        BannerVm.UpdateShiftContext(ShiftGridVm.AllBedTiles);

        string? activeId = BannerVm.SelectedPatientId ?? patientId;
        ShiftGridVm.SyncSelectionToPatient(activeId);
        AnalyticsVm.LoadForPatient(activeId);
        SafetyRxVm.LoadForPatient(activeId);
        RefreshShellSyncIndicator();
    }

    private void OnOpenPatientChartFromSync(string patientId)
    {
        if (!string.IsNullOrWhiteSpace(patientId))
        {
            BannerVm.SelectPatientById(patientId, notifyListeners: false);
            string? effectiveId = BannerVm.SelectedPatientId ?? patientId;
            ShiftGridVm.SyncSelectionToPatient(effectiveId);
            AnalyticsVm.LoadForPatient(effectiveId);
            SafetyRxVm.LoadForPatient(effectiveId);
        }

        ActiveTab = WorkspaceTab.PatientAnalytics;
    }

    private async Task RestartSyncHostWithShellRefreshAsync(LocalSyncBindMode bindMode)
    {
        try
        {
            await _restartWithBindModeAsync(bindMode).ConfigureAwait(true);
        }
        finally
        {
            RefreshShellSyncIndicator();
        }
    }

    private void RefreshActiveWorkspace(WorkspaceTab tab)
    {
        switch (tab)
        {
            case WorkspaceTab.ShiftGrid:
                ShiftGridVm.RefreshShiftBoard();
                BannerVm.UpdateShiftContext(ShiftGridVm.AllBedTiles);
                ShiftGridVm.SyncSelectionToPatient(BannerVm.SelectedPatientId);
                break;
            case WorkspaceTab.PatientAnalytics:
                AnalyticsVm.LoadForPatient(BannerVm.SelectedPatientId);
                break;
            case WorkspaceTab.SafetyRxWorkbench:
                SafetyRxVm.LoadForPatient(BannerVm.SelectedPatientId);
                break;
            case WorkspaceTab.SyncMonitor:
                SyncMonitorVm.RefreshDiagnostics();
                break;
        }

        RefreshShellSyncIndicator();
    }

    private void HandleNavigateParameter(object? parameter)
    {
        if (parameter is WorkspaceTab tab)
        {
            SelectWorkspaceTab(tab);
        }
        else if (parameter is string text && Enum.TryParse<WorkspaceTab>(text, ignoreCase: true, out var parsed))
        {
            SelectWorkspaceTab(parsed);
        }
    }

    private void OnAlarmCadenceTick(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _alarmTickCount++;
        _highFlashOn = !_highFlashOn;
        if ((_alarmTickCount & 1) == 0)
        {
            _mediumFlashOn = !_mediumFlashOn;
        }

        ShiftGridVm.ApplyFlashCadence(_highFlashOn, _mediumFlashOn);

        if (_alarmTickCount % 6 == 0)
        {
            RefreshShellSyncIndicator();
        }
    }
}
