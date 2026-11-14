using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using NephroEdge.Desktop.Infrastructure;
using Persistence_Unit.Entities;
using Persistence_Unit.Interfaces;
using Services_Unit.Core.Contracts;
using Services_Unit.Core.Interfaces;
using Services_Unit.SyncBridge;

namespace NephroEdge.Desktop.ViewModels;

/// <summary>
/// Workspace 4 ViewModel: Order Handshake Conflict Review Queue,
/// Embedded LocalSyncHttpHost Monitor, and Sync Outbox Inspector.
/// Enforces IEC 60601-1-8 separation: technical sync notices use Steady Cyan (#0891B2) or Amber (#D97706),
/// never Red.
/// </summary>
public sealed class SyncMonitorViewModel : ObservableObject, IDisposable
{
    private static readonly IBrush ConfirmedTealSoftBrush = new SolidColorBrush(Color.Parse("#F0FDFA")).ToImmutable();
    private static readonly IBrush ConfirmedTealBorderBrush = new SolidColorBrush(Color.Parse("#0F766E")).ToImmutable();
    private static readonly IBrush ConfirmedTealInkBrush = new SolidColorBrush(Color.Parse("#115E59")).ToImmutable();

    private static readonly IBrush TechCyanSoftBrush = new SolidColorBrush(Color.Parse("#ECFEFF")).ToImmutable();
    private static readonly IBrush TechCyanBorderBrush = new SolidColorBrush(Color.Parse("#0891B2")).ToImmutable();
    private static readonly IBrush TechCyanInkBrush = new SolidColorBrush(Color.Parse("#0E7490")).ToImmutable();

    private static readonly IBrush ConflictAmberSoftBrush = new SolidColorBrush(Color.Parse("#FEF3C7")).ToImmutable();
    private static readonly IBrush ConflictAmberBorderBrush = new SolidColorBrush(Color.Parse("#D97706")).ToImmutable();
    private static readonly IBrush ConflictAmberInkBrush = new SolidColorBrush(Color.Parse("#92400E")).ToImmutable();

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Func<LocalSyncHttpHost?> _getSyncHost;
    private readonly Func<LocalSyncBindMode, Task> _restartWithBindModeAsync;
    private readonly Action<string?>? _onConflictResolved;
    private readonly Action<string>? _onOpenPatientChart;
    private readonly Dictionary<string, PatientSummaryView> _patientLookup = new(StringComparer.Ordinal);

    private LocalSyncHttpHost? _subscribedHost;
    private bool _disposed;

    private bool _isListening;
    private string _baseUri = "Not bound";
    private LocalSyncBindMode _bindMode = LocalSyncBindMode.LocalhostAdbOnly;
    private long _requestsHandled;
    private string? _lastRequestUtc;
    private bool _isSwitchingBindMode;
    private string? _hostOperationFeedback;

    private int _pendingOutboxCount;
    private int _conflictCount;
    private int _pendingAckCount;
    private int _patientLookupRevision;
    private OrderHandshakeRecord? _selectedConflict;
    private string _clinicianStaffId = string.Empty;
    private string _resolutionNotes = string.Empty;
    private OrderHandshakeRecord? _lastResolvedHandshake;
    private string? _resolutionFeedbackMessage;

    public SyncMonitorViewModel(
        IServiceScopeFactory scopeFactory,
        Func<LocalSyncHttpHost?> getSyncHost,
        Func<LocalSyncBindMode, Task> restartWithBindModeAsync,
        Action<string?>? onConflictResolved = null,
        Action<string>? onOpenPatientChart = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _getSyncHost = getSyncHost ?? throw new ArgumentNullException(nameof(getSyncHost));
        _restartWithBindModeAsync = restartWithBindModeAsync ?? throw new ArgumentNullException(nameof(restartWithBindModeAsync));
        _onConflictResolved = onConflictResolved;
        _onOpenPatientChart = onOpenPatientChart;

        RefreshDiagnosticsCommand = new RelayCommand(RefreshDiagnostics);
        ToggleBindModeCommand = new RelayCommand(
            () => _ = SwitchBindModeAsync(
                BindMode == LocalSyncBindMode.LocalhostAdbOnly
                    ? LocalSyncBindMode.ClinicLanBroadcast
                    : LocalSyncBindMode.LocalhostAdbOnly),
            () => !IsSwitchingBindMode);
        SetLocalhostAdbCommand = new RelayCommand(
            () => _ = SwitchBindModeAsync(LocalSyncBindMode.LocalhostAdbOnly),
            () => !IsSwitchingBindMode);
        SetClinicLanCommand = new RelayCommand(
            () => _ = SwitchBindModeAsync(LocalSyncBindMode.ClinicLanBroadcast),
            () => !IsSwitchingBindMode);
        ResolveConflictCommand = new RelayCommand(ExecuteResolveConflict, () => CanResolveSelectedConflict);
        OpenPatientChartCommand = new RelayCommand(ExecuteOpenPatientChart, () => HasSelectedConflict);

        RefreshDiagnostics();
    }

    public ObservableCollection<string> RecentRequestLogs { get; } = new();
    public ObservableCollection<SyncOutboxRecord> PendingOutboxItems { get; } = new();
    public ObservableCollection<OrderHandshakeRecord> ConflictQueueItems { get; } = new();

    public RelayCommand RefreshDiagnosticsCommand { get; }
    public RelayCommand ToggleBindModeCommand { get; }
    public RelayCommand SetLocalhostAdbCommand { get; }
    public RelayCommand SetClinicLanCommand { get; }
    public RelayCommand ResolveConflictCommand { get; }
    public RelayCommand OpenPatientChartCommand { get; }

    public bool IsListening
    {
        get => _isListening;
        private set
        {
            if (SetProperty(ref _isListening, value))
            {
                OnPropertyChanged(nameof(HostStatusBadgeText));
            }
        }
    }

    public string HostStatusBadgeText => IsListening
        ? "◈ Bedside Sync · Connected"
        : "◈ Bedside Sync · Standby";

    public string BaseUri
    {
        get => _baseUri;
        private set => SetProperty(ref _baseUri, value);
    }

    public LocalSyncBindMode BindMode
    {
        get => _bindMode;
        private set
        {
            if (SetProperty(ref _bindMode, value))
            {
                OnPropertyChanged(nameof(BindModeDisplay));
                OnPropertyChanged(nameof(ToggleBindModeButtonText));
                OnPropertyChanged(nameof(IsLocalhostAdbSelected));
                OnPropertyChanged(nameof(IsClinicLanSelected));
            }
        }
    }

    public string BindModeDisplay => BindMode == LocalSyncBindMode.ClinicLanBroadcast
        ? "Clinic Wi-Fi LAN (0.0.0.0)"
        : "USB ADB Bridge (127.0.0.1)";

    public string ToggleBindModeButtonText => BindMode == LocalSyncBindMode.LocalhostAdbOnly
        ? "Switch to Clinic Wi-Fi LAN"
        : "Switch to USB ADB Bridge";

    public bool IsLocalhostAdbSelected => BindMode == LocalSyncBindMode.LocalhostAdbOnly;
    public bool IsClinicLanSelected => BindMode == LocalSyncBindMode.ClinicLanBroadcast;

    public long RequestsHandled
    {
        get => _requestsHandled;
        private set => SetProperty(ref _requestsHandled, value);
    }

    public string? LastRequestUtc
    {
        get => _lastRequestUtc;
        private set
        {
            if (SetProperty(ref _lastRequestUtc, value))
            {
                OnPropertyChanged(nameof(LastRequestUtcDisplay));
            }
        }
    }

    public string LastRequestUtcDisplay => string.IsNullOrWhiteSpace(LastRequestUtc)
        ? "No tablet activity yet"
        : ClinicalDisplayFormatter.FormatHumanTimestamp(LastRequestUtc);

    public bool IsSwitchingBindMode
    {
        get => _isSwitchingBindMode;
        private set
        {
            if (SetProperty(ref _isSwitchingBindMode, value))
            {
                ToggleBindModeCommand.RaiseCanExecuteChanged();
                SetLocalhostAdbCommand.RaiseCanExecuteChanged();
                SetClinicLanCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? HostOperationFeedback
    {
        get => _hostOperationFeedback;
        private set
        {
            if (SetProperty(ref _hostOperationFeedback, value))
            {
                OnPropertyChanged(nameof(HasHostOperationFeedback));
            }
        }
    }

    public bool HasHostOperationFeedback => !string.IsNullOrWhiteSpace(HostOperationFeedback);

    public int PendingOutboxCount
    {
        get => _pendingOutboxCount;
        private set
        {
            if (SetProperty(ref _pendingOutboxCount, value))
            {
                OnPropertyChanged(nameof(OutboxBadgeText));
            }
        }
    }

    public string OutboxBadgeText => PendingOutboxCount > 0
        ? $"◈ {PendingOutboxCount} Pending Sync"
        : "◈ Queue Clear";

    public int ConflictCount
    {
        get => _conflictCount;
        private set
        {
            if (SetProperty(ref _conflictCount, value))
            {
                OnPropertyChanged(nameof(ConflictQueueBadgeText));
                OnPropertyChanged(nameof(ConflictBadgeBackgroundBrush));
                OnPropertyChanged(nameof(ConflictBadgeBorderBrush));
                OnPropertyChanged(nameof(ConflictBadgeForegroundBrush));
            }
        }
    }

    public int PendingAckCount
    {
        get => _pendingAckCount;
        private set
        {
            if (SetProperty(ref _pendingAckCount, value))
            {
                OnPropertyChanged(nameof(ConflictQueueBadgeText));
            }
        }
    }

    public string ConflictQueueBadgeText => ConflictCount > 0
        ? $"◆ {ConflictCount} Conflict(s) · {PendingAckCount} Awaiting Ack"
        : $"◈ {PendingAckCount} Awaiting Nurse Ack";

    public IBrush ConflictBadgeBackgroundBrush => ConflictCount > 0 ? ConflictAmberSoftBrush : TechCyanSoftBrush;
    public IBrush ConflictBadgeBorderBrush => ConflictCount > 0 ? ConflictAmberBorderBrush : TechCyanBorderBrush;
    public IBrush ConflictBadgeForegroundBrush => ConflictCount > 0 ? ConflictAmberInkBrush : TechCyanInkBrush;

    public OrderHandshakeRecord? SelectedConflict
    {
        get => _selectedConflict;
        set
        {
            if (SetProperty(ref _selectedConflict, value))
            {
                RaiseSelectionProperties();
            }
        }
    }

    public bool HasSelectedConflict => SelectedConflict is not null;

    public bool IsSelectedItemConflict =>
        SelectedConflict is not null
        && (string.Equals(SelectedConflict.Status, "CONFLICT_REQUIRES_CLINICIAN_ACK", StringComparison.Ordinal)
            || (!string.IsNullOrWhiteSpace(SelectedConflict.ConflictReason)
                && !string.Equals(SelectedConflict.Status, "PENDING_ACK", StringComparison.Ordinal)
                && !string.Equals(SelectedConflict.Status, "ACKNOWLEDGED", StringComparison.Ordinal)));

    public bool IsSelectedItemConfirmed =>
        SelectedConflict is not null
        && string.Equals(SelectedConflict.Status, "ACKNOWLEDGED", StringComparison.Ordinal);

    public bool IsSelectedItemWaiting =>
        SelectedConflict is not null
        && !IsSelectedItemConflict
        && !IsSelectedItemConfirmed;

    public bool IsConflictResolutionVisible => HasSelectedConflict && IsSelectedItemConflict;

    public bool IsCalmStatusCardVisible => !IsConflictResolutionVisible;

    public int PatientLookupRevision
    {
        get => _patientLookupRevision;
        private set => SetProperty(ref _patientLookupRevision, value);
    }

    public string ResolvePatientNameAndMrn(string? patientId)
    {
        if (string.IsNullOrWhiteSpace(patientId))
        {
            return "Unassigned Patient";
        }

        if (_patientLookup.TryGetValue(patientId, out var patient))
        {
            return $"{patient.FullName} · {patient.MedicalRecordNumber}";
        }

        return patientId;
    }

    public string RightPaneHeaderTitle => IsSelectedItemConflict
        ? "CLINICIAN CONFLICT REVIEW"
        : SelectedConflict is null
            ? "ORDER STATUS INSPECTOR"
            : "ORDER STATUS & CONFIRMATION";

    public IBrush RightPaneHeaderBrush => IsSelectedItemConflict
        ? ConflictAmberInkBrush
        : IsSelectedItemConfirmed
            ? ConfirmedTealInkBrush
            : TechCyanInkBrush;

    public IBrush RightPaneBorderBrush => IsSelectedItemConflict
        ? ConflictAmberBorderBrush
        : IsSelectedItemConfirmed
            ? ConfirmedTealBorderBrush
            : TechCyanBorderBrush;

    public IBrush RightPaneReasonBrush => IsSelectedItemConflict
        ? ConflictAmberInkBrush
        : IsSelectedItemConfirmed
            ? ConfirmedTealInkBrush
            : TechCyanInkBrush;

    public IBrush CalmStatusCardBackgroundBrush => IsSelectedItemConfirmed
        ? ConfirmedTealSoftBrush
        : TechCyanSoftBrush;

    public IBrush CalmStatusCardBorderBrush => IsSelectedItemConfirmed
        ? ConfirmedTealBorderBrush
        : TechCyanBorderBrush;

    public string CalmStatusCardTitle => IsSelectedItemConfirmed
        ? "Confirmed by Bedside Nurse"
        : IsSelectedItemWaiting
            ? "Waiting for Bedside Confirmation"
            : "All Bedside Handshakes Up to Date";

    public string CalmStatusCardBody => IsSelectedItemConfirmed
        ? "This directive has been acknowledged on the bedside tablet with matching sync version."
        : IsSelectedItemWaiting
            ? SelectedConflictReasonDisplay
            : "No conflicts require clinician review. Select an order from the queue to inspect its status.";

    public string SelectedConflictPatientNameDisplay
    {
        get
        {
            if (SelectedConflict is null)
            {
                return "No directive selected";
            }

            if (_patientLookup.TryGetValue(SelectedConflict.PatientId, out var patient))
            {
                return patient.FullName;
            }

            return SelectedConflict.PatientId;
        }
    }

    public string SelectedConflictPatientIdsDisplay
    {
        get
        {
            if (SelectedConflict is null)
            {
                return string.Empty;
            }

            if (_patientLookup.TryGetValue(SelectedConflict.PatientId, out var patient))
            {
                return $"{patient.MedicalRecordNumber} · {SelectedConflict.PatientId}";
            }

            return SelectedConflict.PatientId;
        }
    }

    public string SelectedConflictPatientDisplay
    {
        get
        {
            if (SelectedConflict is null)
            {
                return "No directive selected";
            }

            if (_patientLookup.TryGetValue(SelectedConflict.PatientId, out var patient))
            {
                return $"{patient.FullName} · {patient.MedicalRecordNumber} ({SelectedConflict.PatientId})";
            }

            return SelectedConflict.PatientId;
        }
    }

    public string SelectedConflictSummaryDisplay => SelectedConflict is null
        ? "Select an order handshake from the queue to review details."
        : $"{ClinicalDisplayFormatter.FormatDirectiveType(SelectedConflict.DirectiveType)} · {ClinicalDisplayFormatter.FormatHandshakeStatus(SelectedConflict.Status)}";

    public string SelectedConflictDirectiveDetailDisplay => SelectedConflict?.DirectiveSummary ?? string.Empty;

    public string SelectedConflictFooterDisplay => SelectedConflict is null
        ? string.Empty
        : $"Ref {SelectedConflict.HandshakeId} · v{SelectedConflict.ExpectedSyncVersion}";

    public string SelectedConflictReasonDisplay => SelectedConflict is null
        ? string.Empty
        : !string.IsNullOrWhiteSpace(SelectedConflict.ConflictReason)
            ? ClinicalDisplayFormatter.FormatSyncConflictSummary(SelectedConflict.ConflictReason)
            : IsSelectedItemConfirmed
                ? "Confirmed by bedside nurse tablet — no action required."
                : "Awaiting bedside nurse tablet confirmation — no conflict to resolve.";

    public string ClinicianStaffId
    {
        get => _clinicianStaffId;
        set => SetProperty(ref _clinicianStaffId, value ?? string.Empty);
    }

    public string ResolutionNotes
    {
        get => _resolutionNotes;
        set
        {
            if (SetProperty(ref _resolutionNotes, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanResolveSelectedConflict));
                ResolveConflictCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanResolveSelectedConflict =>
        IsSelectedItemConflict
        && !string.IsNullOrWhiteSpace(ResolutionNotes);

    public OrderHandshakeRecord? LastResolvedHandshake
    {
        get => _lastResolvedHandshake;
        private set
        {
            if (SetProperty(ref _lastResolvedHandshake, value))
            {
                OnPropertyChanged(nameof(HasLastResolvedHandshake));
                OnPropertyChanged(nameof(LastResolvedStatusText));
            }
        }
    }

    public bool HasLastResolvedHandshake => LastResolvedHandshake is not null;

    public string LastResolvedStatusText => LastResolvedHandshake is null
        ? string.Empty
        : $"Ref {LastResolvedHandshake.HandshakeId} · {ClinicalDisplayFormatter.FormatHandshakeStatus(LastResolvedHandshake.Status)} (v{LastResolvedHandshake.ExpectedSyncVersion}) · {ClinicalDisplayFormatter.FormatSyncConflictSummary(LastResolvedHandshake.ConflictReason)}";

    public string? ResolutionFeedbackMessage
    {
        get => _resolutionFeedbackMessage;
        private set
        {
            if (SetProperty(ref _resolutionFeedbackMessage, value))
            {
                OnPropertyChanged(nameof(HasResolutionFeedback));
            }
        }
    }

    public bool HasResolutionFeedback => !string.IsNullOrWhiteSpace(ResolutionFeedbackMessage);

    public bool HasNoQueueItems => ConflictQueueItems.Count == 0;
    public bool HasNoOutboxItems => PendingOutboxItems.Count == 0;
    public bool HasNoRequestLogs => RecentRequestLogs.Count == 0;

    public void FocusDirective(string? patientOrHandshakeId)
    {
        if (string.IsNullOrWhiteSpace(patientOrHandshakeId) || ConflictQueueItems.Count == 0)
        {
            return;
        }

        string target = patientOrHandshakeId.Trim();
        OrderHandshakeRecord? match =
            ConflictQueueItems.FirstOrDefault(d => string.Equals(d.HandshakeId, target, StringComparison.Ordinal))
            ?? ConflictQueueItems.FirstOrDefault(d =>
                string.Equals(d.PatientId, target, StringComparison.Ordinal)
                && string.Equals(d.Status, "CONFLICT_REQUIRES_CLINICIAN_ACK", StringComparison.Ordinal))
            ?? ConflictQueueItems.FirstOrDefault(d => string.Equals(d.PatientId, target, StringComparison.Ordinal))
            ?? ConflictQueueItems.FirstOrDefault(d => string.Equals(d.SessionId, target, StringComparison.Ordinal));

        if (match is not null)
        {
            SelectedConflict = match;
        }
    }

    public void RefreshDiagnostics()
    {
        if (_disposed)
        {
            return;
        }

        var host = _getSyncHost();
        SyncHostSubscription(host);

        if (host is not null)
        {
            ApplyHostSnapshot(host.GetDiagnosticsSnapshot());
        }
        else
        {
            IsListening = false;
            BaseUri = "Bridge in offline standby";
            BindMode = LocalSyncBindMode.LocalhostAdbOnly;
            RequestsHandled = 0;
            LastRequestUtc = null;
            RecentRequestLogs.Clear();
            OnPropertyChanged(nameof(HasNoRequestLogs));
        }

        LoadDatabaseQueues();
    }

    private void LoadDatabaseQueues()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var syncQueueRepository = scope.ServiceProvider.GetRequiredService<ISyncQueueRepository>();
            var patientService = scope.ServiceProvider.GetRequiredService<IPatientService>();
            var handshakeService = scope.ServiceProvider.GetRequiredService<IOrderHandshakeService>();

            var pendingBatch = syncQueueRepository.GetPendingOutboxBatch(50);
            PendingOutboxItems.Clear();
            foreach (var item in pendingBatch)
            {
                PendingOutboxItems.Add(item);
            }
            PendingOutboxCount = pendingBatch.Count;
            OnPropertyChanged(nameof(HasNoOutboxItems));

            var patients = patientService.GetAllPatientSummaries();
            _patientLookup.Clear();
            var openDirectives = new Dictionary<string, OrderHandshakeRecord>(StringComparer.Ordinal);

            foreach (var patient in patients)
            {
                _patientLookup[patient.PatientId] = patient;
                var handshakes = handshakeService.GetDirectivesForPatient(patient.PatientId);
                foreach (var directive in handshakes)
                {
                    if (string.Equals(directive.Status, "CONFLICT_REQUIRES_CLINICIAN_ACK", StringComparison.Ordinal)
                        || string.Equals(directive.Status, "PENDING_ACK", StringComparison.Ordinal))
                    {
                        openDirectives[directive.HandshakeId] = directive;
                    }
                }
            }

            var orderedDirectives = openDirectives.Values
                .OrderByDescending(d => string.Equals(d.Status, "CONFLICT_REQUIRES_CLINICIAN_ACK", StringComparison.Ordinal))
                .ThenByDescending(d => d.CreatedAtUtc, StringComparer.Ordinal)
                .ToList();

            string? priorSelectedId = SelectedConflict?.HandshakeId;
            PatientLookupRevision++;

            ConflictQueueItems.Clear();
            foreach (var item in orderedDirectives)
            {
                ConflictQueueItems.Add(item);
            }

            ConflictCount = orderedDirectives.Count(d =>
                string.Equals(d.Status, "CONFLICT_REQUIRES_CLINICIAN_ACK", StringComparison.Ordinal));
            PendingAckCount = orderedDirectives.Count(d =>
                string.Equals(d.Status, "PENDING_ACK", StringComparison.Ordinal));

            OrderHandshakeRecord? nextSelected = priorSelectedId is not null
                ? orderedDirectives.FirstOrDefault(d => string.Equals(d.HandshakeId, priorSelectedId, StringComparison.Ordinal))
                : null;

            nextSelected ??= orderedDirectives.FirstOrDefault(d =>
                string.Equals(d.Status, "CONFLICT_REQUIRES_CLINICIAN_ACK", StringComparison.Ordinal))
                ?? orderedDirectives.FirstOrDefault();

            SelectedConflict = nextSelected;
            RaiseSelectionProperties();
            OnPropertyChanged(nameof(HasNoQueueItems));
        }
        catch (Exception ex)
        {
            HostOperationFeedback = $"Queue refresh notice: {ex.Message}";
        }
    }

    private void ExecuteResolveConflict()
    {
        if (SelectedConflict is null)
        {
            ResolutionFeedbackMessage = "Select an order conflict from the queue before confirming.";
            return;
        }

        if (!string.Equals(SelectedConflict.Status, "CONFLICT_REQUIRES_CLINICIAN_ACK", StringComparison.Ordinal))
        {
            ResolutionFeedbackMessage = "Selected directive is awaiting initial nurse acknowledgement (no conflict to resolve).";
            return;
        }

        if (string.IsNullOrWhiteSpace(ResolutionNotes))
        {
            ResolutionFeedbackMessage = "Clinical verification rationale is required before confirming.";
            return;
        }

        try
        {
            string clinicianId = !string.IsNullOrWhiteSpace(ClinicianStaffId)
                ? ClinicianStaffId.Trim()
                : SelectedConflict.CreatedByDoctorId;

            using var scope = _scopeFactory.CreateScope();
            var handshakeService = scope.ServiceProvider.GetRequiredService<IOrderHandshakeService>();

            var resolved = handshakeService.ResolveConflict(new ClosedLoopConflictResolutionRequest(
                HandshakeId: SelectedConflict.HandshakeId,
                ClinicianStaffId: clinicianId,
                ResolutionNotes: ResolutionNotes.Trim()));

            if (resolved is null)
            {
                ResolutionFeedbackMessage = $"Directive '{SelectedConflict.HandshakeId}' was not found.";
                return;
            }

            LastResolvedHandshake = resolved;
            ResolutionFeedbackMessage =
                $"✓ Order Confirmed by {clinicianId} (v{resolved.ExpectedSyncVersion})";
            ResolutionNotes = string.Empty;

            RefreshDiagnostics();
            _onConflictResolved?.Invoke(resolved.PatientId);
        }
        catch (Exception ex)
        {
            ResolutionFeedbackMessage = $"Conflict resolution blocked: {ex.Message}";
        }
    }

    private void ExecuteOpenPatientChart()
    {
        if (SelectedConflict is null || string.IsNullOrWhiteSpace(SelectedConflict.PatientId))
        {
            return;
        }

        _onOpenPatientChart?.Invoke(SelectedConflict.PatientId);
    }

    private async Task SwitchBindModeAsync(LocalSyncBindMode targetMode)
    {
        if (IsSwitchingBindMode)
        {
            return;
        }

        string modeLabel = targetMode == LocalSyncBindMode.ClinicLanBroadcast
            ? "Clinic Wi-Fi LAN"
            : "USB ADB Bridge";

        IsSwitchingBindMode = true;
        HostOperationFeedback = $"◈ Switching bedside sync bridge to {modeLabel}...";

        try
        {
            await _restartWithBindModeAsync(targetMode);
            HostOperationFeedback = $"◈ Bedside sync bridge active on {modeLabel}.";
        }
        catch (Exception ex)
        {
            HostOperationFeedback =
                $"◈ Sync notice: Could not switch to {modeLabel} ({ex.Message}). USB ADB bridge remains available.";
        }
        finally
        {
            IsSwitchingBindMode = false;
            RefreshDiagnostics();
        }
    }

    private void SyncHostSubscription(LocalSyncHttpHost? host)
    {
        if (ReferenceEquals(_subscribedHost, host))
        {
            return;
        }

        if (_subscribedHost is not null)
        {
            _subscribedHost.DiagnosticsUpdated -= OnHostDiagnosticsUpdated;
        }

        _subscribedHost = host;
        if (_subscribedHost is not null)
        {
            _subscribedHost.DiagnosticsUpdated += OnHostDiagnosticsUpdated;
        }
    }

    private void OnHostDiagnosticsUpdated(object? sender, SyncHostDiagnosticsSnapshot snapshot)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
            {
                ApplyHostSnapshot(snapshot);
                LoadDatabaseQueues();
            }
            else
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_disposed)
                    {
                        ApplyHostSnapshot(snapshot);
                        LoadDatabaseQueues();
                    }
                });
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or TaskCanceledException)
        {
            // Expected background HttpListener/dispatcher races during window or host teardown.
        }
    }

    private void ApplyHostSnapshot(SyncHostDiagnosticsSnapshot snapshot)
    {
        IsListening = snapshot.IsListening;
        BaseUri = snapshot.BaseUri;
        BindMode = snapshot.BindMode;
        RequestsHandled = snapshot.RequestsHandled;
        LastRequestUtc = snapshot.LastRequestUtc;

        RecentRequestLogs.Clear();
        for (int i = snapshot.RecentRequestLogs.Count - 1; i >= 0; i--)
        {
            RecentRequestLogs.Add(snapshot.RecentRequestLogs[i]);
        }
        OnPropertyChanged(nameof(HasNoRequestLogs));
    }

    private void RaiseSelectionProperties()
    {
        OnPropertyChanged(nameof(HasSelectedConflict));
        OnPropertyChanged(nameof(IsSelectedItemConflict));
        OnPropertyChanged(nameof(IsSelectedItemConfirmed));
        OnPropertyChanged(nameof(IsSelectedItemWaiting));
        OnPropertyChanged(nameof(IsConflictResolutionVisible));
        OnPropertyChanged(nameof(IsCalmStatusCardVisible));
        OnPropertyChanged(nameof(RightPaneHeaderTitle));
        OnPropertyChanged(nameof(RightPaneHeaderBrush));
        OnPropertyChanged(nameof(RightPaneBorderBrush));
        OnPropertyChanged(nameof(RightPaneReasonBrush));
        OnPropertyChanged(nameof(CalmStatusCardBackgroundBrush));
        OnPropertyChanged(nameof(CalmStatusCardBorderBrush));
        OnPropertyChanged(nameof(CalmStatusCardTitle));
        OnPropertyChanged(nameof(CalmStatusCardBody));
        OnPropertyChanged(nameof(SelectedConflictPatientNameDisplay));
        OnPropertyChanged(nameof(SelectedConflictPatientIdsDisplay));
        OnPropertyChanged(nameof(SelectedConflictPatientDisplay));
        OnPropertyChanged(nameof(SelectedConflictSummaryDisplay));
        OnPropertyChanged(nameof(SelectedConflictDirectiveDetailDisplay));
        OnPropertyChanged(nameof(SelectedConflictFooterDisplay));
        OnPropertyChanged(nameof(SelectedConflictReasonDisplay));
        OnPropertyChanged(nameof(CanResolveSelectedConflict));
        ResolveConflictCommand.RaiseCanExecuteChanged();
        OpenPatientChartCommand.RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_subscribedHost is not null)
        {
            _subscribedHost.DiagnosticsUpdated -= OnHostDiagnosticsUpdated;
            _subscribedHost = null;
        }
    }
}
