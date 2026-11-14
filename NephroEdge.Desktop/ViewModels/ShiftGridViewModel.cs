using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using NephroEdge.Desktop.Infrastructure;
using Persistence_Unit.Entities;
using Persistence_Unit.Interfaces;
using Services_Unit.Core.Interfaces;
using Services_Unit.RiskPrediction.Contracts;
using Services_Unit.RiskPrediction.Interfaces;

namespace NephroEdge.Desktop.ViewModels;

/// <summary>
/// Workspace 1 ViewModel: Active 12-Bed Shift Risk Grid.
/// Queries live active shift sessions, telemetry ticks, historical nadir SBP lags,
/// RiskPredictionModule evaluations, and closed-loop OrderHandshakes using Scoped DI.
/// </summary>
public sealed class ShiftGridViewModel : ObservableObject
{
    private static readonly IBrush TechCyanSoftBrush = new SolidColorBrush(Color.Parse("#ECFEFF")).ToImmutable();
    private static readonly IBrush TechCyanBorderBrush = new SolidColorBrush(Color.Parse("#0E7490")).ToImmutable();
    private static readonly IBrush TechCyanInkBrush = new SolidColorBrush(Color.Parse("#155E75")).ToImmutable();

    private static readonly IBrush ConflictAmberSoftBrush = new SolidColorBrush(Color.Parse("#FEF3C7")).ToImmutable();
    private static readonly IBrush ConflictAmberBorderBrush = new SolidColorBrush(Color.Parse("#D97706")).ToImmutable();
    private static readonly IBrush ConflictAmberInkBrush = new SolidColorBrush(Color.Parse("#92400E")).ToImmutable();

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Action<string, WorkspaceTab?> _onSelectPatientAndNavigate;
    private readonly Action<string?, string?>? _onOpenSyncMonitorAndFocus;
    private readonly List<ShiftBedTileItem> _allBeds = new();

    private string _sessionDate = "2026-11-10";
    private int _shiftNumber = 1;
    private string _filterText = string.Empty;
    private string _selectedRiskFilter = "All";
    private ShiftBedTileItem? _selectedBed;
    private string? _selectedSessionId;
    private double _bedCardWidth = 340.0;

    private int _totalBedCount;
    private int _highAlarmCount;
    private int _mediumWarningCount;
    private int _nominalBedCount;
    private int _isolatedBedCount;
    private int _pendingHandshakeCount;
    private int _conflictHandshakeCount;
    private string? _loadError;
    private string _lastRefreshedDisplay = "Not loaded";
    private string _shiftSyncNoticeText = "◈ Bedside Orders Synced";
    private IBrush _shiftSyncNoticeBackgroundBrush = TechCyanSoftBrush;
    private IBrush _shiftSyncNoticeBorderBrush = TechCyanBorderBrush;
    private IBrush _shiftSyncNoticeForegroundBrush = TechCyanInkBrush;

    private bool _highFlashOn = true;
    private bool _mediumFlashOn = true;

    public ShiftGridViewModel(
        IServiceScopeFactory scopeFactory,
        Action<string, WorkspaceTab?> onSelectPatientAndNavigate,
        Action<string?, string?>? onOpenSyncMonitorAndFocus = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _onSelectPatientAndNavigate = onSelectPatientAndNavigate ?? throw new ArgumentNullException(nameof(onSelectPatientAndNavigate));
        _onOpenSyncMonitorAndFocus = onOpenSyncMonitorAndFocus;

        RefreshCommand = new RelayCommand(RefreshShiftBoard);
        SelectBedCommand = new RelayCommand(param => HandleBedAction(param, null));
        OpenAnalyticsCommand = new RelayCommand(param => HandleBedAction(param, WorkspaceTab.PatientAnalytics));
        OpenSafetyRxCommand = new RelayCommand(param => HandleBedAction(param, WorkspaceTab.SafetyRxWorkbench));
        PrimaryActionCommand = new RelayCommand(ExecutePrimaryAction);
        OpenSyncMonitorCommand = new RelayCommand(OpenSyncMonitor);
        OpenBedSyncMonitorCommand = new RelayCommand(OpenBedSyncMonitor);
        ClearSearchCommand = new RelayCommand(() => FilterText = string.Empty);
        OpenFirstMatchingBedCommand = new RelayCommand(ExecuteOpenFirstMatchingBed);
        SetRiskFilterCommand = new RelayCommand(param =>
        {
            if (param is string filter && !string.IsNullOrWhiteSpace(filter))
            {
                SelectedRiskFilter = filter;
            }
        });
        SetShiftNumberCommand = new RelayCommand(param =>
        {
            if (param is int shiftInt)
            {
                ShiftNumber = shiftInt;
            }
            else if (param is string shiftText && int.TryParse(shiftText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                ShiftNumber = parsed;
            }
        });

        RefreshShiftBoard();
    }

    public ObservableCollection<ShiftBedTileItem> FilteredBeds { get; } = new();
    public IReadOnlyList<ShiftBedTileItem> AllBedTiles => _allBeds;

    public IReadOnlyList<int> AvailableShifts { get; } = new[] { 1, 2, 3, 4 };

    public IReadOnlyList<string> AvailableRiskFilters { get; } = new[] { "All", "HighRedOnly", "WarningOnly", "IsolatedOnly" };

    public RelayCommand RefreshCommand { get; }
    public RelayCommand SelectBedCommand { get; }
    public RelayCommand OpenAnalyticsCommand { get; }
    public RelayCommand OpenSafetyRxCommand { get; }
    public RelayCommand PrimaryActionCommand { get; }
    public RelayCommand OpenSyncMonitorCommand { get; }
    public RelayCommand OpenBedSyncMonitorCommand { get; }
    public RelayCommand ClearSearchCommand { get; }
    public RelayCommand OpenFirstMatchingBedCommand { get; }
    public RelayCommand SetRiskFilterCommand { get; }
    public RelayCommand SetShiftNumberCommand { get; }

    public string ShiftBedSummaryDisplay =>
        string.Create(CultureInfo.InvariantCulture, $"{TotalBedCount} beds · {SessionDate} · Shift {ShiftNumber}");

    public double BedCardWidth
    {
        get => _bedCardWidth;
        private set => SetProperty(ref _bedCardWidth, value);
    }

    public string SessionDate
    {
        get => _sessionDate;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (SetProperty(ref _sessionDate, normalized))
            {
                OnPropertyChanged(nameof(ShiftBedSummaryDisplay));
                RefreshShiftBoard();
            }
        }
    }

    public int ShiftNumber
    {
        get => _shiftNumber;
        set
        {
            int clamped = Math.Clamp(value, 1, 4);
            if (SetProperty(ref _shiftNumber, clamped))
            {
                OnPropertyChanged(nameof(ShiftBedSummaryDisplay));
                RefreshShiftBoard();
            }
        }
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value ?? string.Empty))
            {
                ApplyFilters();
            }
        }
    }

    public string SelectedRiskFilter
    {
        get => _selectedRiskFilter;
        set
        {
            string normalized = value switch
            {
                "HighRedOnly" => "HighRedOnly",
                "WarningOnly" => "WarningOnly",
                "IsolatedOnly" => "IsolatedOnly",
                _ => "All"
            };

            if (SetProperty(ref _selectedRiskFilter, normalized))
            {
                OnPropertyChanged(nameof(IsAllFilterSelected));
                OnPropertyChanged(nameof(IsHighRedFilterSelected));
                OnPropertyChanged(nameof(IsWarningFilterSelected));
                OnPropertyChanged(nameof(IsIsolatedFilterSelected));
                ApplyFilters();
            }
        }
    }

    public bool IsAllFilterSelected => string.Equals(SelectedRiskFilter, "All", StringComparison.Ordinal);
    public bool IsHighRedFilterSelected => string.Equals(SelectedRiskFilter, "HighRedOnly", StringComparison.Ordinal);
    public bool IsWarningFilterSelected => string.Equals(SelectedRiskFilter, "WarningOnly", StringComparison.Ordinal);
    public bool IsIsolatedFilterSelected => string.Equals(SelectedRiskFilter, "IsolatedOnly", StringComparison.Ordinal);

    public ShiftBedTileItem? SelectedBed
    {
        get => _selectedBed;
        set
        {
            if (SetProperty(ref _selectedBed, value))
            {
                _selectedSessionId = value?.SessionId;
                for (int i = 0; i < _allBeds.Count; i++)
                {
                    _allBeds[i].IsSelected = ReferenceEquals(_allBeds[i], value);
                }
            }
        }
    }

    public int TotalBedCount
    {
        get => _totalBedCount;
        private set
        {
            if (SetProperty(ref _totalBedCount, value))
            {
                OnPropertyChanged(nameof(ShiftBedSummaryDisplay));
            }
        }
    }

    public int HighAlarmCount
    {
        get => _highAlarmCount;
        private set => SetProperty(ref _highAlarmCount, value);
    }

    public int MediumWarningCount
    {
        get => _mediumWarningCount;
        private set => SetProperty(ref _mediumWarningCount, value);
    }

    public int NominalBedCount
    {
        get => _nominalBedCount;
        private set => SetProperty(ref _nominalBedCount, value);
    }

    public int IsolatedBedCount
    {
        get => _isolatedBedCount;
        private set => SetProperty(ref _isolatedBedCount, value);
    }

    public int PendingHandshakeCount
    {
        get => _pendingHandshakeCount;
        private set => SetProperty(ref _pendingHandshakeCount, value);
    }

    public int ConflictHandshakeCount
    {
        get => _conflictHandshakeCount;
        private set => SetProperty(ref _conflictHandshakeCount, value);
    }

    public string? LoadError
    {
        get => _loadError;
        private set
        {
            if (SetProperty(ref _loadError, value))
            {
                OnPropertyChanged(nameof(HasLoadError));
            }
        }
    }

    public bool HasLoadError => !string.IsNullOrWhiteSpace(LoadError);

    public bool HasNoFilteredBeds => FilteredBeds.Count == 0;

    public string LastRefreshedDisplay
    {
        get => _lastRefreshedDisplay;
        private set => SetProperty(ref _lastRefreshedDisplay, value);
    }

    public string ShiftSyncNoticeText
    {
        get => _shiftSyncNoticeText;
        private set => SetProperty(ref _shiftSyncNoticeText, value);
    }

    public IBrush ShiftSyncNoticeBackgroundBrush
    {
        get => _shiftSyncNoticeBackgroundBrush;
        private set => SetProperty(ref _shiftSyncNoticeBackgroundBrush, value);
    }

    public IBrush ShiftSyncNoticeBorderBrush
    {
        get => _shiftSyncNoticeBorderBrush;
        private set => SetProperty(ref _shiftSyncNoticeBorderBrush, value);
    }

    public IBrush ShiftSyncNoticeForegroundBrush
    {
        get => _shiftSyncNoticeForegroundBrush;
        private set => SetProperty(ref _shiftSyncNoticeForegroundBrush, value);
    }

    public void UpdateCardWidthForViewport(double viewportWidth)
    {
        if (!double.IsFinite(viewportWidth) || viewportWidth <= 0.0)
        {
            return;
        }

        double usable = Math.Max(300.0, viewportWidth - 18.0);
        int columns = usable >= 1460.0 ? 4
                    : usable >= 980.0 ? 3
                    : usable >= 640.0 ? 2
                    : 1;
        const double gap = 12.0;
        double computed = Math.Floor((usable - (columns * gap)) / columns);
        BedCardWidth = Math.Clamp(computed, 290.0, 420.0);
    }

    public void SyncSelectionToPatient(string? patientId)
    {
        if (string.IsNullOrWhiteSpace(patientId))
        {
            SelectedBed = null;
            return;
        }

        if (_selectedBed is not null &&
            string.Equals(_selectedBed.PatientId, patientId, StringComparison.Ordinal))
        {
            return;
        }

        var match = _allBeds.FirstOrDefault(b =>
            string.Equals(b.PatientId, patientId, StringComparison.Ordinal));
        if (match is not null)
        {
            SelectedBed = match;
        }
    }

    public void ApplyFlashCadence(bool highFlashOn, bool mediumFlashOn)
    {
        _highFlashOn = highFlashOn;
        _mediumFlashOn = mediumFlashOn;

        for (int i = 0; i < _allBeds.Count; i++)
        {
            _allBeds[i].ApplyFlashState(highFlashOn, mediumFlashOn);
        }
    }

    public void RefreshShiftBoard()
    {
        if (string.IsNullOrWhiteSpace(SessionDate))
        {
            _allBeds.Clear();
            FilteredBeds.Clear();
            ResetSummaryMetrics();
            LoadError = "Session date is required (YYYY-MM-DD).";
            OnPropertyChanged(nameof(HasNoFilteredBeds));
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sessionService = scope.ServiceProvider.GetRequiredService<ISessionService>();
            var sessionRepository = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
            var patientService = scope.ServiceProvider.GetRequiredService<IPatientService>();
            var riskModule = scope.ServiceProvider.GetRequiredService<IRiskPredictionModule>();
            var handshakeService = scope.ServiceProvider.GetRequiredService<IOrderHandshakeService>();

            var boardRows = sessionService.GetActiveShiftBoard(SessionDate, ShiftNumber);
            var shiftDirectives = handshakeService.GetPendingDirectivesForShift(SessionDate, ShiftNumber);

            var builtTiles = new List<ShiftBedTileItem>(boardRows.Count);
            foreach (var row in boardRows)
            {
                var tile = BuildBedTile(
                    row,
                    shiftDirectives,
                    sessionService,
                    sessionRepository,
                    patientService,
                    riskModule);
                tile.ApplyFlashState(_highFlashOn, _mediumFlashOn);
                builtTiles.Add(tile);
            }

            var orderedTiles = builtTiles
                .OrderByDescending(GetAlarmSortRank)
                .ThenBy(b => b.ChairOrBedNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _allBeds.Clear();
            _allBeds.AddRange(orderedTiles);

            TotalBedCount = _allBeds.Count;
            HighAlarmCount = _allBeds.Count(b => b.IsHighAlarm);
            MediumWarningCount = _allBeds.Count(b => b.IsMediumWarning);
            NominalBedCount = _allBeds.Count(b => b.IsNominal);
            IsolatedBedCount = _allBeds.Count(b => b.IsIsolationRequired);
            PendingHandshakeCount = shiftDirectives.Count;
            ConflictHandshakeCount = shiftDirectives.Count(d =>
                string.Equals(d.Status, "CONFLICT_REQUIRES_CLINICIAN_ACK", StringComparison.Ordinal));

            UpdateSyncNoticePill();
            LoadError = null;
            LastRefreshedDisplay = $"Updated {DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}";

            ApplyFilters();
            RestoreSelection();
        }
        catch (Exception ex)
        {
            _allBeds.Clear();
            FilteredBeds.Clear();
            ResetSummaryMetrics();
            LoadError = $"Unable to load shift board ({SessionDate} / Shift {ShiftNumber}): {ex.Message}";
            OnPropertyChanged(nameof(HasNoFilteredBeds));
        }
    }

    private ShiftBedTileItem BuildBedTile(
        ActiveSessionBoardView row,
        IReadOnlyList<OrderHandshakeRecord> shiftDirectives,
        ISessionService sessionService,
        ISessionRepository sessionRepository,
        IPatientService patientService,
        IRiskPredictionModule riskModule)
    {
        var ticks = sessionService.GetSessionTelemetry(row.SessionId);
        var nadirLags = sessionRepository.GetRecentPatientSessionNadirSbp(row.PatientId, 10);
        var patient = patientService.GetPatientClinicalSummary(row.PatientId);
        var activeRx = patientService.GetActivePrescription(row.PatientId);

        var tickSamples = ticks.Select(t => new IntradialyticTelemetryTickSample(
            ElapsedMinutes: checked((int)t.ElapsedMinutes),
            SbpMmHg: t.SystolicBp,
            DbpMmHg: t.DiastolicBp,
            HrBpm: t.HeartRateBpm,
            UfrMlHr: t.UltrafiltrationRateMlHr,
            CumulativeUfRemovedMl: t.CumulativeUfRemovedMl,
            DialysateTempC: t.DialysateTemperatureCelsius,
            DialysateConductivityMsCm: t.DialysateConductivityMsCm,
            BloodFlowRateMlMin: t.BloodFlowRateMlMin,
            DialysateFlowRateMlMin: t.DialysateFlowRateMlMin,
            ArterialPressureMmHg: t.ArterialPressureMmHg,
            VenousPressureMmHg: t.VenousPressureMmHg,
            TmpMmHg: t.TransmembranePressureMmHg,
            IsInvasiveLine: false)).ToList();

        IntradialyticTelemetryRecord? latestTick = ticks.Count > 0 ? ticks[^1] : null;
        int ageYears = patient is not null && patient.CalculatedAgeYears > 0
            ? Math.Clamp((int)patient.CalculatedAgeYears, 1, 120)
            : 55;
        int sexCode = string.Equals(patient?.BiologicalSex, "F", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
        double safeDryWeightKg = row.TargetDryWeightKg > 0.0
            ? row.TargetDryWeightKg
            : Math.Max(20.0, row.PreDialysisWeightKg - (row.TargetUltrafiltrationMl / 1000.0));

        var hybridInput = new IntradialyticRiskScoringHybridInput(
            SessionId: row.SessionId,
            PatientId: row.PatientId,
            AgeYears: ageYears,
            SexCode: sexCode,
            TargetDryWeightKg: safeDryWeightKg,
            PreSessionWeightKg: row.PreDialysisWeightKg,
            TargetUfMl: row.TargetUltrafiltrationMl,
            PreSessionSbpMmHg: row.PreSystolicBp,
            PreSessionDbpMmHg: row.PreDiastolicBp,
            PreSessionHrBpm: row.PreHeartRateBpm,
            DialysateTempC: latestTick?.DialysateTemperatureCelsius ?? 36.5,
            DialysateConductivityMsCm: latestTick?.DialysateConductivityMsCm ?? activeRx?.DialysateSodiumMeqL / 10.0 ?? 14.0,
            BloodFlowRateMlMin: latestTick?.BloodFlowRateMlMin ?? activeRx?.PrescribedBloodFlowMlMin ?? 300.0,
            DialysateFlowRateMlMin: latestTick?.DialysateFlowRateMlMin ?? activeRx?.PrescribedDialysateFlowMlMin ?? 500.0,
            ArterialPressureMmHg: latestTick?.ArterialPressureMmHg ?? -120.0,
            VenousPressureMmHg: latestTick?.VenousPressureMmHg ?? 120.0,
            TmpMmHg: latestTick?.TransmembranePressureMmHg ?? 80.0,
            RawTelemetryWindowTicks: tickSamples,
            HistoricalSessionNadirSbpLags: nadirLags);

        var assessment = riskModule
            .EvaluateSessionRiskAsync(hybridInput, preferRemoteAi: false)
            .GetAwaiter()
            .GetResult();

        long latestSbp = row.LatestSystolicBp ?? latestTick?.SystolicBp ?? row.PreSystolicBp;
        long latestDbp = row.LatestDiastolicBp ?? latestTick?.DiastolicBp ?? row.PreDiastolicBp;
        long latestHr = row.LatestHeartRateBpm ?? latestTick?.HeartRateBpm ?? row.PreHeartRateBpm;
        double deltaSbp = Math.Max(assessment.FeatureVector.DeltaSbpFromPreBaselineMmHg, row.PreSystolicBp - latestSbp);
        double latestUfr = row.LatestUfRateMlHr ?? latestTick?.UltrafiltrationRateMlHr ?? (row.TargetUltrafiltrationMl / 4.0);
        double remainingUf = row.RemainingUfMl ?? Math.Max(0.0, row.TargetUltrafiltrationMl - (latestTick?.CumulativeUfRemovedMl ?? 0.0));

        AlarmPriorityTier effectivePriority = ResolveEffectiveAlarmPriority(
            latestSbp,
            deltaSbp,
            row.ActiveUnresolvedIncidentCount,
            assessment);

        string triggerSummary = BuildTop1TriggerSummary(
            assessment,
            latestSbp,
            deltaSbp,
            row.ActiveUnresolvedIncidentCount);

        var bedDirectives = shiftDirectives
            .Where(d => string.Equals(d.SessionId, row.SessionId, StringComparison.Ordinal)
                     || (d.SessionId is null && string.Equals(d.PatientId, row.PatientId, StringComparison.Ordinal)))
            .ToList();

        OrderHandshakeRecord? priorityDirective =
            bedDirectives.FirstOrDefault(d => string.Equals(d.Status, "CONFLICT_REQUIRES_CLINICIAN_ACK", StringComparison.Ordinal))
            ?? bedDirectives.FirstOrDefault();

        string pendingAckBadgeText = priorityDirective is not null &&
            string.Equals(priorityDirective.Status, "CONFLICT_REQUIRES_CLINICIAN_ACK", StringComparison.Ordinal)
            ? "Order Conflict"
            : bedDirectives.Count > 0
                ? "Pending Nurse Ack"
                : "Orders Confirmed";

        string conciseStatusText = BuildConciseStatusText(
            effectivePriority,
            latestSbp,
            latestDbp,
            deltaSbp,
            row.ActiveUnresolvedIncidentCount,
            pendingAckBadgeText);

        return new ShiftBedTileItem(
            boardRow: row,
            riskAssessment: assessment,
            effectiveAlarmPriority: effectivePriority,
            latestSystolicBp: latestSbp,
            latestDiastolicBp: latestDbp,
            latestHeartRateBpm: latestHr,
            deltaSbpMmHg: deltaSbp,
            latestUfRateMlHr: latestUfr,
            remainingUfMl: remainingUf,
            hasTelemetryTicks: ticks.Count > 0 || row.LatestSystolicBp.HasValue,
            triggerRuleSummary: triggerSummary,
            pendingAckBadgeText: pendingAckBadgeText,
            pendingHandshakeCount: bedDirectives.Count,
            primaryHandshakeId: priorityDirective?.HandshakeId,
            conciseStatusText: conciseStatusText);
    }

    private static string BuildConciseStatusText(
        AlarmPriorityTier effectivePriority,
        long latestSbp,
        long latestDbp,
        double deltaSbp,
        long unresolvedIncidentCount,
        string pendingAckBadgeText)
    {
        string raw = effectivePriority switch
        {
            AlarmPriorityTier.HighRed when latestSbp < 90 && deltaSbp >= 20.0 =>
                string.Create(CultureInfo.InvariantCulture, $"Low BP {latestSbp}/{latestDbp} (dropped {deltaSbp:0} mmHg)"),
            AlarmPriorityTier.HighRed when latestSbp < 90 =>
                string.Create(CultureInfo.InvariantCulture, $"Low systolic BP at {latestSbp} mmHg"),
            AlarmPriorityTier.HighRed when deltaSbp >= 20.0 =>
                string.Create(CultureInfo.InvariantCulture, $"BP dropped {deltaSbp:0} mmHg from baseline"),
            AlarmPriorityTier.HighRed =>
                "Immediate bedside review needed",
            AlarmPriorityTier.MediumYellow when unresolvedIncidentCount > 0 =>
                string.Create(CultureInfo.InvariantCulture, $"{unresolvedIncidentCount} open clinical {(unresolvedIncidentCount == 1 ? "incident" : "incidents")}"),
            AlarmPriorityTier.MediumYellow when string.Equals(pendingAckBadgeText, "Order Conflict", StringComparison.Ordinal) =>
                "Order conflict needs review",
            AlarmPriorityTier.MediumYellow when deltaSbp > 10.0 =>
                string.Create(CultureInfo.InvariantCulture, $"BP trending down (-{deltaSbp:0} mmHg)"),
            AlarmPriorityTier.MediumYellow =>
                "Elevated risk · monitor closely",
            _ when string.Equals(pendingAckBadgeText, "Order Conflict", StringComparison.Ordinal) =>
                "Order conflict needs review",
            _ when string.Equals(pendingAckBadgeText, "Pending Nurse Ack", StringComparison.Ordinal) =>
                "Pending nurse confirmation",
            _ =>
                "Vitals stable · UF on track"
        };

        return raw.Length <= 55 ? raw : string.Concat(raw.AsSpan(0, 52), "...");
    }

    private static AlarmPriorityTier ResolveEffectiveAlarmPriority(
        long latestSbp,
        double deltaSbp,
        long unresolvedIncidentCount,
        IntradialyticRiskAssessmentResult assessment)
    {
        if (latestSbp < 90 || deltaSbp >= 20.0 || assessment.AlarmPriority == AlarmPriorityTier.HighRed)
        {
            return AlarmPriorityTier.HighRed;
        }

        if (assessment.AlarmPriority == AlarmPriorityTier.MediumYellow
            || unresolvedIncidentCount > 0
            || assessment.RiskBand >= RiskBand.Moderate)
        {
            return AlarmPriorityTier.MediumYellow;
        }

        return AlarmPriorityTier.None;
    }

    private static string BuildTop1TriggerSummary(
        IntradialyticRiskAssessmentResult assessment,
        long latestSbp,
        double deltaSbp,
        long unresolvedIncidentCount)
    {
        if (assessment.TriggeredRuleIds.Count > 0 && assessment.Top3Drivers.Count > 0)
        {
            var top = assessment.Top3Drivers[0];
            string ruleLabel = ClinicalDisplayFormatter.FormatRuleLabel(assessment.TriggeredRuleIds[0]);
            string driverLabel = ClinicalDisplayFormatter.FormatDriverName(top.FeatureName);
            string observed = ClinicalDisplayFormatter.FormatCleanMeasurementText(top.ObservedValueText);
            return $"{ruleLabel}: {driverLabel} {observed}";
        }

        if (latestSbp < 90)
        {
            return $"Low BP Alert (< 90 mmHg): Latest Systolic BP {latestSbp} mmHg";
        }

        if (deltaSbp >= 20.0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"Rapid BP Drop (>= 20 mmHg): SBP Drop -{deltaSbp:0.#} mmHg");
        }

        if (unresolvedIncidentCount > 0)
        {
            return $"{unresolvedIncidentCount} open clinical incident(s)";
        }

        if (assessment.Top3Drivers.Count > 0)
        {
            var baseline = assessment.Top3Drivers[0];
            string driverLabel = ClinicalDisplayFormatter.FormatDriverName(baseline.FeatureName);
            string observed = ClinicalDisplayFormatter.FormatCleanMeasurementText(baseline.ObservedValueText);
            return $"Stable: {driverLabel} {observed}";
        }

        return "Within nominal dialysis limits";
    }

    private static int GetAlarmSortRank(ShiftBedTileItem item) =>
        item.EffectiveAlarmPriority switch
        {
            AlarmPriorityTier.HighRed => 2,
            AlarmPriorityTier.MediumYellow => 1,
            _ => 0
        };

    private void ApplyFilters()
    {
        IEnumerable<ShiftBedTileItem> query = _allBeds;

        if (string.Equals(SelectedRiskFilter, "HighRedOnly", StringComparison.Ordinal))
        {
            query = query.Where(b => b.IsHighAlarm);
        }
        else if (string.Equals(SelectedRiskFilter, "WarningOnly", StringComparison.Ordinal))
        {
            query = query.Where(b => b.IsMediumWarning);
        }
        else if (string.Equals(SelectedRiskFilter, "IsolatedOnly", StringComparison.Ordinal))
        {
            query = query.Where(b => b.IsIsolationRequired);
        }

        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            string term = FilterText.Trim();
            query = query.Where(b =>
                b.ChairOrBedNumber.Contains(term, StringComparison.OrdinalIgnoreCase)
                || b.PatientFullName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || b.MedicalRecordNumber.Contains(term, StringComparison.OrdinalIgnoreCase)
                || b.PatientId.Contains(term, StringComparison.OrdinalIgnoreCase)
                || b.ConciseStatusText.Contains(term, StringComparison.OrdinalIgnoreCase)
                || b.TriggerRuleSummary.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        FilteredBeds.Clear();
        foreach (var item in query)
        {
            FilteredBeds.Add(item);
        }

        OnPropertyChanged(nameof(HasNoFilteredBeds));
    }

    private void RestoreSelection()
    {
        if (string.IsNullOrWhiteSpace(_selectedSessionId))
        {
            return;
        }

        var match = _allBeds.FirstOrDefault(b => string.Equals(b.SessionId, _selectedSessionId, StringComparison.Ordinal));
        if (match is not null)
        {
            SelectedBed = match;
        }
    }

    private void UpdateSyncNoticePill()
    {
        if (ConflictHandshakeCount > 0)
        {
            ShiftSyncNoticeText = $"◆ {ConflictHandshakeCount} Order Conflict(s) Need Review";
            ShiftSyncNoticeBackgroundBrush = ConflictAmberSoftBrush;
            ShiftSyncNoticeBorderBrush = ConflictAmberBorderBrush;
            ShiftSyncNoticeForegroundBrush = ConflictAmberInkBrush;
            return;
        }

        if (PendingHandshakeCount > 0)
        {
            ShiftSyncNoticeText = $"◈ {PendingHandshakeCount} Nurse Ack(s) Pending";
        }
        else
        {
            ShiftSyncNoticeText = "◈ Bedside Orders Synced";
        }

        ShiftSyncNoticeBackgroundBrush = TechCyanSoftBrush;
        ShiftSyncNoticeBorderBrush = TechCyanBorderBrush;
        ShiftSyncNoticeForegroundBrush = TechCyanInkBrush;
    }

    private void ResetSummaryMetrics()
    {
        TotalBedCount = 0;
        HighAlarmCount = 0;
        MediumWarningCount = 0;
        NominalBedCount = 0;
        IsolatedBedCount = 0;
        PendingHandshakeCount = 0;
        ConflictHandshakeCount = 0;
        UpdateSyncNoticePill();
    }

    private void ExecutePrimaryAction(object? parameter)
    {
        ShiftBedTileItem? tile = ResolveTileParameter(parameter);
        if (tile is null)
        {
            return;
        }

        WorkspaceTab target = tile.IsHighAlarm || tile.IsMediumWarning
            ? WorkspaceTab.SafetyRxWorkbench
            : WorkspaceTab.PatientAnalytics;

        HandleBedAction(tile, target);
    }

    private ShiftBedTileItem? ResolveTileParameter(object? parameter) =>
        parameter switch
        {
            ShiftBedTileItem item => item,
            string id => _allBeds.FirstOrDefault(b =>
                string.Equals(b.SessionId, id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(b.PatientId, id, StringComparison.OrdinalIgnoreCase)),
            _ => SelectedBed
        };

    private void HandleBedAction(object? parameter, WorkspaceTab? targetTab)
    {
        ShiftBedTileItem? tile = ResolveTileParameter(parameter);
        if (tile is null)
        {
            return;
        }

        SelectedBed = tile;
        _onSelectPatientAndNavigate(tile.PatientId, targetTab);
    }

    private void ExecuteOpenFirstMatchingBed()
    {
        if (FilteredBeds.Count > 0)
        {
            HandleBedAction(FilteredBeds[0], null);
        }
        else
        {
            RefreshShiftBoard();
        }
    }

    private void OpenBedSyncMonitor(object? parameter)
    {
        if (parameter is ShiftBedTileItem tile)
        {
            SelectedBed = tile;
            string? focusTarget = tile.PrimaryHandshakeId ?? tile.PatientId;
            if (_onOpenSyncMonitorAndFocus is not null)
            {
                _onOpenSyncMonitorAndFocus(tile.PatientId, focusTarget);
                return;
            }

            _onSelectPatientAndNavigate(tile.PatientId, WorkspaceTab.SyncMonitor);
            return;
        }

        OpenSyncMonitor();
    }

    private void OpenSyncMonitor()
    {
        ShiftBedTileItem? priorityTile =
            _allBeds.FirstOrDefault(b => b.HasConflictHandshake)
            ?? _allBeds.FirstOrDefault(b => b.HasPendingHandshake)
            ?? SelectedBed
            ?? _allBeds.FirstOrDefault();

        string? patientId = priorityTile?.PatientId;
        string? focusTarget = priorityTile?.PrimaryHandshakeId ?? patientId;

        if (_onOpenSyncMonitorAndFocus is not null)
        {
            _onOpenSyncMonitorAndFocus(patientId, focusTarget);
            return;
        }

        _onSelectPatientAndNavigate(patientId ?? string.Empty, WorkspaceTab.SyncMonitor);
    }
}
