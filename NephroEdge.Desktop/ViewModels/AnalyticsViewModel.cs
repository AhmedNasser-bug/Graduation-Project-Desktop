using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NephroEdge.Desktop.Controls;
using NephroEdge.Desktop.Infrastructure;
using Persistence_Unit.Entities;
using Persistence_Unit.Interfaces;
using Services_Unit.Analytics.Contracts;
using Services_Unit.Analytics.Engine;
using Services_Unit.Analytics.Interfaces;
using Services_Unit.Core.Formatting;
using Services_Unit.Core.Interfaces;

namespace NephroEdge.Desktop.ViewModels;

/// <summary>
/// ViewModel for Patient Longitudinal Analytics:
/// Laboratory Trends + Verified Lab Entry;
/// Recent Dialysis Sessions Hemodynamic Time Series (5/10/20) + Pre/Nadir SBP Chart;
/// Daugirdas II spKt/V Calculator, Target Dry Weight History, and Hardware-Deferred Notice.
/// </summary>
public sealed class AnalyticsViewModel : ObservableObject
{
    private static readonly ConcurrentDictionary<string, string> LoincDisplayLookup = new(StringComparer.OrdinalIgnoreCase);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Action<string>? _onClinicalDataUpdated;
    private string? _currentPatientId;
    private DateTime _lastLabTimestampUtc = DateTime.MinValue;

    private bool _hasPatientSelected;
    private string _patientHeaderLabel = "Select a patient from the Shift Grid or Patient Banner to review clinical trends.";

    // Progressive Disclosure Tabs & Drawers (collapsed by default)
    private bool _isLabsTabActive = true;
    private bool _isAddLabDrawerOpen;
    private bool _isKtvDrawerOpen;
    private bool _isDryWeightLogOpen;

    // Laboratory Trends & Entry
    private string _selectedLoincFilter = "ALL";
    private LabSeriesTrend? _selectedLabTrend;
    private string _labChartTitle = "Laboratory Trend";
    private string _labChartUnit = string.Empty;
    private IReadOnlyList<ChartPointItem> _labChartPoints = Array.Empty<ChartPointItem>();
    private double? _labNormalLow;
    private double? _labNormalHigh;
    private double? _labPanicLow;
    private double? _labPanicHigh;
    private string _labEmptyStateMessage = "No laboratory results recorded yet for this patient.";
    private string _selectedLabSummaryText = "Select an analyte or record a verified lab result.";
    private bool _selectedLabHasPanic;

    private string _addLabLoincCode = "2823-3";
    private LoincLabCatalogItem? _selectedAddLabCatalog;
    private string _addLabValue = string.Empty;
    private string _addLabPhase = "Pre-Dialysis";
    private string _labSaveStatusMessage = string.Empty;
    private bool _isLabSaveError;
    private bool _isLabSaveCritical;

    // Recent Sessions Series
    private int _selectedSessionWindowCount = 10;
    private PatientSessionSeriesSummary? _sessionSeriesSummary;
    private IReadOnlyList<ChartPointItem> _sessionPreSbpPoints = Array.Empty<ChartPointItem>();
    private IReadOnlyList<ChartPointItem> _sessionNadirSbpPoints = Array.Empty<ChartPointItem>();
    private string _sessionEmptyStateMessage = "No completed dialysis sessions found for this patient.";
    private string _sessionAvgIdwgText = "--";
    private string _sessionAvgPreSbpText = "--";
    private string _sessionAvgPostSbpText = "--";
    private string _sessionAvgUfText = "--";
    private string _sessionLowestNadirText = "--";
    private string _sessionLatestKtVText = "--";
    private string _lowestNadirSeverityLevel = "Normal";
    private string _latestKtVSeverityLevel = "Normal";

    // Daugirdas II spKt/V Calculator & Target Dry Weight History
    private string _ktVPreBun = string.Empty;
    private string _ktVPostBun = string.Empty;
    private string _ktVDurationHours = string.Empty;
    private string _ktVUfLiters = string.Empty;
    private string _ktVPostWeightKg = string.Empty;
    private DaugirdasKtVResult? _ktVResult;
    private string? _ktVErrorMessage;
    private string _ktVSpKtVFormatted = "--";
    private string _ktVUrrFormatted = "--";
    private string _ktVStatusBadgeText = "Awaiting Input";
    private string _ktVFormattedInputsSummary = string.Empty;
    private bool _ktVIsInadequateWarning;

    public AnalyticsViewModel(
        IServiceScopeFactory scopeFactory,
        Action<string>? onClinicalDataUpdated = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _onClinicalDataUpdated = onClinicalDataUpdated;

        SaveLabResultCommand = new RelayCommand(ExecuteSaveLabResult);
        SetSessionWindowCommand = new RelayCommand(ExecuteSetSessionWindow);
        ComputeKtVCommand = new RelayCommand(ExecuteComputeKtV);
        RefreshAnalyticsCommand = new RelayCommand(() => LoadForPatient(_currentPatientId));
        SelectLabsTabCommand = new RelayCommand(() => IsLabsTabActive = true);
        SelectSessionsTabCommand = new RelayCommand(() => IsLabsTabActive = false);
        ToggleAddLabDrawerCommand = new RelayCommand(() => IsAddLabDrawerOpen = !IsAddLabDrawerOpen);
        ToggleKtvDrawerCommand = new RelayCommand(() => IsKtvDrawerOpen = !IsKtvDrawerOpen);
        ToggleDryWeightLogCommand = new RelayCommand(() => IsDryWeightLogOpen = !IsDryWeightLogOpen);
        SelectBiomarkerCommand = new RelayCommand(ExecuteSelectBiomarker);

        LoadCatalogMetadata();
    }

    public static string FormatLoincFilterLabel(string? loincCode)
    {
        if (string.IsNullOrWhiteSpace(loincCode) ||
            string.Equals(loincCode.Trim(), "ALL", StringComparison.OrdinalIgnoreCase))
        {
            return "All Analytes";
        }

        string trimmed = loincCode.Trim();
        return LoincDisplayLookup.TryGetValue(trimmed, out string? analyteName)
            ? $"{analyteName} ({trimmed})"
            : trimmed;
    }

    public ObservableCollection<string> AvailableLoincFilters { get; } = new() { "ALL" };

    public ObservableCollection<LoincLabCatalogItem> CatalogLoincItems { get; } = new();

    public ObservableCollection<string> SamplingPhases { get; } = new()
    {
        "Pre-Dialysis",
        "Post-Dialysis",
        "Mid-Dialysis",
        "Interdialytic"
    };

    public IReadOnlyList<int> SessionWindowOptions { get; } = new[] { 5, 10, 20 };

    public ObservableCollection<LabSeriesTrend> LabTrends { get; } = new();

    public ObservableCollection<SessionSeriesPoint> SessionRows { get; } = new();

    public ObservableCollection<DryWeightHistoryRecord> DryWeightHistory { get; } = new();

    public RelayCommand SaveLabResultCommand { get; }

    public RelayCommand SetSessionWindowCommand { get; }

    public RelayCommand ComputeKtVCommand { get; }

    public RelayCommand RefreshAnalyticsCommand { get; }

    public RelayCommand SelectLabsTabCommand { get; }

    public RelayCommand SelectSessionsTabCommand { get; }

    public RelayCommand ToggleAddLabDrawerCommand { get; }

    public RelayCommand ToggleKtvDrawerCommand { get; }

    public RelayCommand ToggleDryWeightLogCommand { get; }

    public RelayCommand SelectBiomarkerCommand { get; }

    public bool IsLabsTabActive
    {
        get => _isLabsTabActive;
        set
        {
            if (SetProperty(ref _isLabsTabActive, value))
            {
                OnPropertyChanged(nameof(IsSessionsTabActive));
            }
        }
    }

    public bool IsSessionsTabActive
    {
        get => !_isLabsTabActive;
        set => IsLabsTabActive = !value;
    }

    public bool IsAddLabDrawerOpen
    {
        get => _isAddLabDrawerOpen;
        set
        {
            if (SetProperty(ref _isAddLabDrawerOpen, value))
            {
                OnPropertyChanged(nameof(AddLabDrawerButtonText));
            }
        }
    }

    public string AddLabDrawerButtonText => IsAddLabDrawerOpen ? "Close Lab Entry" : "+ Add Lab Result";

    public bool IsKtvDrawerOpen
    {
        get => _isKtvDrawerOpen;
        set => SetProperty(ref _isKtvDrawerOpen, value);
    }

    public bool IsDryWeightLogOpen
    {
        get => _isDryWeightLogOpen;
        set => SetProperty(ref _isDryWeightLogOpen, value);
    }

    public bool HasPatientSelected
    {
        get => _hasPatientSelected;
        private set => SetProperty(ref _hasPatientSelected, value);
    }

    public string PatientHeaderLabel
    {
        get => _patientHeaderLabel;
        private set => SetProperty(ref _patientHeaderLabel, value);
    }

    public string SelectedLoincFilter
    {
        get => _selectedLoincFilter;
        set
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? "ALL" : value.Trim();
            if (SetProperty(ref _selectedLoincFilter, normalized))
            {
                NotifyBiomarkerPillStates();
                ReloadLabTrends();
            }
        }
    }

    public bool IsAllLabsPillActive => string.Equals(SelectedLoincFilter, "ALL", StringComparison.OrdinalIgnoreCase);
    public bool IsPotassiumPillActive => string.Equals(SelectedLoincFilter, "2823-3", StringComparison.OrdinalIgnoreCase);
    public bool IsHemoglobinPillActive => string.Equals(SelectedLoincFilter, "718-7", StringComparison.OrdinalIgnoreCase);
    public bool IsPhosphorusPillActive => string.Equals(SelectedLoincFilter, "2777-1", StringComparison.OrdinalIgnoreCase);
    public bool IsPthPillActive => string.Equals(SelectedLoincFilter, "2731-8", StringComparison.OrdinalIgnoreCase);

    public LabSeriesTrend? SelectedLabTrend
    {
        get => _selectedLabTrend;
        set
        {
            if (SetProperty(ref _selectedLabTrend, value))
            {
                UpdateLabChartBinding();
            }
        }
    }

    public string LabChartTitle
    {
        get => _labChartTitle;
        private set => SetProperty(ref _labChartTitle, value);
    }

    public string LabChartUnit
    {
        get => _labChartUnit;
        private set => SetProperty(ref _labChartUnit, value);
    }

    public IReadOnlyList<ChartPointItem> LabChartPoints
    {
        get => _labChartPoints;
        private set => SetProperty(ref _labChartPoints, value);
    }

    public double? LabNormalLow
    {
        get => _labNormalLow;
        private set => SetProperty(ref _labNormalLow, value);
    }

    public double? LabNormalHigh
    {
        get => _labNormalHigh;
        private set => SetProperty(ref _labNormalHigh, value);
    }

    public double? LabPanicLow
    {
        get => _labPanicLow;
        private set => SetProperty(ref _labPanicLow, value);
    }

    public double? LabPanicHigh
    {
        get => _labPanicHigh;
        private set => SetProperty(ref _labPanicHigh, value);
    }

    public string LabEmptyStateMessage
    {
        get => _labEmptyStateMessage;
        private set => SetProperty(ref _labEmptyStateMessage, value);
    }

    public string SelectedLabSummaryText
    {
        get => _selectedLabSummaryText;
        private set => SetProperty(ref _selectedLabSummaryText, value);
    }

    public bool SelectedLabHasPanic
    {
        get => _selectedLabHasPanic;
        private set => SetProperty(ref _selectedLabHasPanic, value);
    }

    public bool HasLabTrends => LabTrends.Count > 0;
    public bool HasMultipleLabTrends => LabTrends.Count > 1;
    public bool HasSessionRows => SessionRows.Count > 0;
    public bool HasDryWeightHistory => DryWeightHistory.Count > 0;

    public string AddLabLoincCode
    {
        get => _addLabLoincCode;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (SetProperty(ref _addLabLoincCode, normalized))
            {
                SyncSelectedCatalogFromCode(normalized);
            }
        }
    }

    public LoincLabCatalogItem? SelectedAddLabCatalog
    {
        get => _selectedAddLabCatalog;
        set
        {
            if (SetProperty(ref _selectedAddLabCatalog, value) && value is not null)
            {
                SetProperty(ref _addLabLoincCode, value.LoincCode, nameof(AddLabLoincCode));
            }
        }
    }

    public string AddLabValue
    {
        get => _addLabValue;
        set => SetProperty(ref _addLabValue, value ?? string.Empty);
    }

    public string AddLabPhase
    {
        get => _addLabPhase;
        set => SetProperty(ref _addLabPhase, string.IsNullOrWhiteSpace(value) ? "Pre-Dialysis" : value.Trim());
    }

    public string LabSaveStatusMessage
    {
        get => _labSaveStatusMessage;
        private set => SetProperty(ref _labSaveStatusMessage, value);
    }

    public bool IsLabSaveError
    {
        get => _isLabSaveError;
        private set => SetProperty(ref _isLabSaveError, value);
    }

    public bool IsLabSaveCritical
    {
        get => _isLabSaveCritical;
        private set => SetProperty(ref _isLabSaveCritical, value);
    }

    public int SelectedSessionWindowCount
    {
        get => _selectedSessionWindowCount;
        set
        {
            int clamped = value is 5 or 10 or 20 ? value : 10;
            if (SetProperty(ref _selectedSessionWindowCount, clamped))
            {
                OnPropertyChanged(nameof(IsWindow5Selected));
                OnPropertyChanged(nameof(IsWindow10Selected));
                OnPropertyChanged(nameof(IsWindow20Selected));
                ReloadSessionSeries();
            }
        }
    }

    public bool IsWindow5Selected => SelectedSessionWindowCount == 5;
    public bool IsWindow10Selected => SelectedSessionWindowCount == 10;
    public bool IsWindow20Selected => SelectedSessionWindowCount == 20;

    public PatientSessionSeriesSummary? SessionSeriesSummary
    {
        get => _sessionSeriesSummary;
        private set => SetProperty(ref _sessionSeriesSummary, value);
    }

    public IReadOnlyList<ChartPointItem> SessionPreSbpPoints
    {
        get => _sessionPreSbpPoints;
        private set => SetProperty(ref _sessionPreSbpPoints, value);
    }

    public IReadOnlyList<ChartPointItem> SessionNadirSbpPoints
    {
        get => _sessionNadirSbpPoints;
        private set => SetProperty(ref _sessionNadirSbpPoints, value);
    }

    public string SessionEmptyStateMessage
    {
        get => _sessionEmptyStateMessage;
        private set => SetProperty(ref _sessionEmptyStateMessage, value);
    }

    public string SessionAvgIdwgText
    {
        get => _sessionAvgIdwgText;
        private set => SetProperty(ref _sessionAvgIdwgText, value);
    }

    public string SessionAvgPreSbpText
    {
        get => _sessionAvgPreSbpText;
        private set => SetProperty(ref _sessionAvgPreSbpText, value);
    }

    public string SessionAvgPostSbpText
    {
        get => _sessionAvgPostSbpText;
        private set => SetProperty(ref _sessionAvgPostSbpText, value);
    }

    public string SessionAvgUfText
    {
        get => _sessionAvgUfText;
        private set => SetProperty(ref _sessionAvgUfText, value);
    }

    public string SessionLowestNadirText
    {
        get => _sessionLowestNadirText;
        private set => SetProperty(ref _sessionLowestNadirText, value);
    }

    public string SessionLatestKtVText
    {
        get => _sessionLatestKtVText;
        private set => SetProperty(ref _sessionLatestKtVText, value);
    }

    public string LowestNadirSeverityLevel
    {
        get => _lowestNadirSeverityLevel;
        private set => SetProperty(ref _lowestNadirSeverityLevel, value);
    }

    public string LatestKtVSeverityLevel
    {
        get => _latestKtVSeverityLevel;
        private set => SetProperty(ref _latestKtVSeverityLevel, value);
    }

    public string KtVPreBun
    {
        get => _ktVPreBun;
        set => SetProperty(ref _ktVPreBun, value ?? string.Empty);
    }

    public string KtVPostBun
    {
        get => _ktVPostBun;
        set => SetProperty(ref _ktVPostBun, value ?? string.Empty);
    }

    public string KtVDurationHours
    {
        get => _ktVDurationHours;
        set => SetProperty(ref _ktVDurationHours, value ?? string.Empty);
    }

    public string KtVUfLiters
    {
        get => _ktVUfLiters;
        set => SetProperty(ref _ktVUfLiters, value ?? string.Empty);
    }

    public string KtVPostWeightKg
    {
        get => _ktVPostWeightKg;
        set => SetProperty(ref _ktVPostWeightKg, value ?? string.Empty);
    }

    public DaugirdasKtVResult? KtVResult
    {
        get => _ktVResult;
        private set => SetProperty(ref _ktVResult, value);
    }

    public string? KtVErrorMessage
    {
        get => _ktVErrorMessage;
        private set => SetProperty(ref _ktVErrorMessage, value);
    }

    public string KtVSpKtVFormatted
    {
        get => _ktVSpKtVFormatted;
        private set => SetProperty(ref _ktVSpKtVFormatted, value);
    }

    public string KtVUrrFormatted
    {
        get => _ktVUrrFormatted;
        private set => SetProperty(ref _ktVUrrFormatted, value);
    }

    public string KtVStatusBadgeText
    {
        get => _ktVStatusBadgeText;
        private set => SetProperty(ref _ktVStatusBadgeText, value);
    }

    public string KtVFormattedInputsSummary
    {
        get => _ktVFormattedInputsSummary;
        private set => SetProperty(ref _ktVFormattedInputsSummary, value);
    }

    public bool KtVIsInadequateWarning
    {
        get => _ktVIsInadequateWarning;
        private set => SetProperty(ref _ktVIsInadequateWarning, value);
    }

    public string HardwareDeferredNoticeTitle => "Hardware Telemetry Deferred — Bedside Sensors Not Connected";

    public string HardwareDeferredNoticeBody =>
        "Bioimpedance hydration (OH/TBW, Phase Angle) and Reverse Osmosis (RO) water conductivity telemetry remain deferred until bedside hardware drivers are integrated. Zero simulated sensor values are emitted.";

    public event EventHandler? PatientContextSwitched;

    public void LoadForPatient(string? patientId)
    {
        bool patientChanged = !string.Equals(_currentPatientId, patientId, StringComparison.Ordinal);
        _currentPatientId = string.IsNullOrWhiteSpace(patientId) ? null : patientId.Trim();

        if (patientChanged)
        {
            ClearTransientPatientFields();
            PatientContextSwitched?.Invoke(this, EventArgs.Empty);
        }

        LoadCatalogMetadata();

        if (_currentPatientId is null)
        {
            HasPatientSelected = false;
            PatientHeaderLabel = "Select a patient from the Shift Grid or Patient Banner to review clinical trends.";
            LabTrends.Clear();
            SelectedLabTrend = null;
            OnPropertyChanged(nameof(HasLabTrends));
            OnPropertyChanged(nameof(HasMultipleLabTrends));
            SessionRows.Clear();
            OnPropertyChanged(nameof(HasSessionRows));
            SessionSeriesSummary = null;
            SessionPreSbpPoints = Array.Empty<ChartPointItem>();
            SessionNadirSbpPoints = Array.Empty<ChartPointItem>();
            SessionAvgIdwgText = "--";
            SessionAvgPreSbpText = "--";
            SessionAvgPostSbpText = "--";
            SessionAvgUfText = "--";
            SessionLowestNadirText = "--";
            SessionLatestKtVText = "--";
            LowestNadirSeverityLevel = "Normal";
            LatestKtVSeverityLevel = "Normal";
            DryWeightHistory.Clear();
            OnPropertyChanged(nameof(HasDryWeightHistory));
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var patientService = scope.ServiceProvider.GetRequiredService<IPatientService>();
        var summary = patientService.GetPatientClinicalSummary(_currentPatientId);

        HasPatientSelected = summary is not null;
        PatientHeaderLabel = summary is not null
            ? $"{summary.FullName} ({summary.MedicalRecordNumber}) — Clinical Trends & Adequacy"
            : $"Patient {_currentPatientId} — Clinical Trends & Adequacy";

        ReloadLabTrends(scope);
        ReloadSessionSeries(scope);
        ReloadDryWeightTimeline(scope);
    }

    private void ClearTransientPatientFields()
    {
        IsAddLabDrawerOpen = false;
        IsKtvDrawerOpen = false;
        IsDryWeightLogOpen = false;

        AddLabValue = string.Empty;
        LabSaveStatusMessage = string.Empty;
        IsLabSaveError = false;
        IsLabSaveCritical = false;
        SelectedLabTrend = null;

        KtVPreBun = string.Empty;
        KtVPostBun = string.Empty;
        KtVDurationHours = string.Empty;
        KtVUfLiters = string.Empty;
        KtVPostWeightKg = string.Empty;
        KtVResult = null;
        KtVErrorMessage = null;
        KtVSpKtVFormatted = "--";
        KtVUrrFormatted = "--";
        KtVStatusBadgeText = "Awaiting Input";
        KtVFormattedInputsSummary = string.Empty;
        KtVIsInadequateWarning = false;
    }

    private void LoadCatalogMetadata()
    {
        using var scope = _scopeFactory.CreateScope();
        var catalogRepo = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();
        var definitions = catalogRepo.GetAllLoincLabDefinitions();

        foreach (var item in definitions)
        {
            LoincDisplayLookup[item.LoincCode] = item.AnalyteName;
        }

        if (CatalogLoincItems.Count == 0 && definitions.Count > 0)
        {
            foreach (var item in definitions)
            {
                CatalogLoincItems.Add(item);
                if (!AvailableLoincFilters.Contains(item.LoincCode))
                {
                    AvailableLoincFilters.Add(item.LoincCode);
                }
            }

            SyncSelectedCatalogFromCode(_addLabLoincCode);
        }
    }

    private void SyncSelectedCatalogFromCode(string loincCode)
    {
        if (CatalogLoincItems.Count == 0)
        {
            return;
        }

        var match = CatalogLoincItems.FirstOrDefault(c =>
            string.Equals(c.LoincCode, loincCode, StringComparison.OrdinalIgnoreCase));
        if (match is not null && !Equals(_selectedAddLabCatalog, match))
        {
            SetProperty(ref _selectedAddLabCatalog, match, nameof(SelectedAddLabCatalog));
        }
    }

    private void ReloadLabTrends(IServiceScope? existingScope = null)
    {
        if (_currentPatientId is null)
        {
            LabTrends.Clear();
            SelectedLabTrend = null;
            OnPropertyChanged(nameof(HasLabTrends));
            OnPropertyChanged(nameof(HasMultipleLabTrends));
            return;
        }

        using var ownedScope = existingScope is null ? _scopeFactory.CreateScope() : null;
        var sp = (existingScope ?? ownedScope)!.ServiceProvider;
        var labAnalyzer = sp.GetRequiredService<ILabTimeSeriesAnalyzer>();

        string? loincParam = string.Equals(SelectedLoincFilter, "ALL", StringComparison.OrdinalIgnoreCase)
            ? null
            : SelectedLoincFilter;

        var trends = labAnalyzer.GetLabTrendsForPatient(_currentPatientId, loincParam);
        string? previousLoinc = SelectedLabTrend?.LoincCode;

        LabTrends.Clear();
        foreach (var trend in trends)
        {
            LabTrends.Add(trend);
        }

        OnPropertyChanged(nameof(HasLabTrends));
        OnPropertyChanged(nameof(HasMultipleLabTrends));

        if (LabTrends.Count == 0)
        {
            SelectedLabTrend = null;
            LabEmptyStateMessage = loincParam is null
                ? "No lab results recorded yet. Click \"+ Add Lab Result\" to log a reading."
                : $"No recorded results for {FormatLoincFilterLabel(loincParam)} yet. Click \"+ Add Lab Result\" to add one.";
            UpdateLabChartBinding();
            return;
        }

        SelectedLabTrend = LabTrends.FirstOrDefault(t =>
                               string.Equals(t.LoincCode, previousLoinc, StringComparison.OrdinalIgnoreCase))
                           ?? LabTrends[0];
    }

    private void UpdateLabChartBinding()
    {
        if (SelectedLabTrend is null)
        {
            LabChartTitle = "Laboratory Trend";
            LabChartUnit = string.Empty;
            LabNormalLow = null;
            LabNormalHigh = null;
            LabPanicLow = null;
            LabPanicHigh = null;
            LabChartPoints = Array.Empty<ChartPointItem>();
            SelectedLabSummaryText = "No laboratory analyte observations recorded yet.";
            SelectedLabHasPanic = false;
            return;
        }

        var trend = SelectedLabTrend;
        LabChartTitle = $"{trend.AnalyteName} ({trend.LoincCode})";
        LabChartUnit = trend.CanonicalUnit;
        LabNormalLow = trend.NormalRangeLow;
        LabNormalHigh = trend.NormalRangeHigh;
        LabPanicLow = trend.CriticalPanicLow;
        LabPanicHigh = trend.CriticalPanicHigh;

        LabChartPoints = BuildDisambiguatedLabPoints(trend);
        SelectedLabHasPanic = trend.HasPanicPoint;

        string latestStr = trend.LatestValue.HasValue
            ? FormatIsmpValue(trend.LatestValue.Value, trend.CanonicalUnit)
            : "--";
        string panicTag = trend.HasPanicPoint ? " · Critical Panic Alert" : string.Empty;

        if (trend.LatestDelta.HasValue)
        {
            string deltaStr = trend.LatestDelta.Value >= 0
                ? $"+{trend.LatestDelta.Value.ToString("0.##", CultureInfo.InvariantCulture)} {trend.CanonicalUnit}"
                : $"{trend.LatestDelta.Value.ToString("0.##", CultureInfo.InvariantCulture)} {trend.CanonicalUnit}";
            string trendLabel = ClinicalDisplayFormatter.FormatLabTrendDirection(trend.TrendDirection);
            SelectedLabSummaryText = ClinicalDisplayFormatter.FormatCleanMeasurementText(
                $"{trend.AnalyteName}: Latest {latestStr} · Delta {deltaStr} · {trendLabel}{panicTag}");
        }
        else
        {
            int count = trend.Points.Count;
            string baselineLabel = count == 1 ? "Initial baseline (1 reading)" : $"Initial baseline ({count} readings)";
            SelectedLabSummaryText = ClinicalDisplayFormatter.FormatCleanMeasurementText(
                $"{trend.AnalyteName}: Latest {latestStr} · {baselineLabel}{panicTag}");
        }
    }

    private static List<ChartPointItem> BuildDisambiguatedLabPoints(LabSeriesTrend trend)
    {
        var points = new List<ChartPointItem>(trend.Points.Count);
        if (trend.Points.Count == 0)
        {
            return points;
        }

        var parsed = new (LabSeriesPoint Point, string DateKey, string MinuteKey, string HumanTs)[trend.Points.Count];
        var dateCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var minuteCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int i = 0; i < trend.Points.Count; i++)
        {
            var p = trend.Points[i];
            bool ok = DateTimeOffset.TryParse(
                p.SpecimenCollectionUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset dto);

            string dateKey = ok
                ? dto.ToString("MM-dd", CultureInfo.InvariantCulture)
                : (p.SpecimenCollectionUtc.Length >= 10 ? p.SpecimenCollectionUtc[5..10] : p.SpecimenCollectionUtc);
            string minuteKey = ok
                ? dto.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture)
                : dateKey;
            string humanTs = ok
                ? dto.ToString("MMM dd · HH:mm 'UTC'", CultureInfo.InvariantCulture)
                : ClinicalDisplayFormatter.FormatHumanTimestamp(p.SpecimenCollectionUtc);

            parsed[i] = (p, dateKey, minuteKey, humanTs);
            dateCounts[dateKey] = dateCounts.TryGetValue(dateKey, out int dc) ? dc + 1 : 1;
            minuteCounts[minuteKey] = minuteCounts.TryGetValue(minuteKey, out int mc) ? mc + 1 : 1;
        }

        var minuteOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < parsed.Length; i++)
        {
            var (p, dateKey, minuteKey, humanTs) = parsed[i];
            int dateTotal = dateCounts[dateKey];
            int minTotal = minuteCounts[minuteKey];

            string xLabel;
            string fullTimestamp = humanTs;
            if (dateTotal <= 1)
            {
                xLabel = dateKey;
            }
            else if (minTotal <= 1)
            {
                xLabel = minuteKey;
            }
            else
            {
                int ord = minuteOrdinals.TryGetValue(minuteKey, out int cur) ? cur + 1 : 1;
                minuteOrdinals[minuteKey] = ord;
                xLabel = $"{dateKey} #{ord}";
                fullTimestamp = $"{humanTs} (#{ord})";
            }

            string phaseLabel = ClinicalDisplayFormatter.FormatSamplingPhase(p.SamplingPhase);
            string flagLabel = ClinicalDisplayFormatter.FormatAbnormalityFlag(p.AbnormalityFlag);
            string deltaPart = p.DeltaFromPrevious is double d
                ? $" · {(d >= 0 ? "+" : string.Empty)}{d.ToString("0.##", CultureInfo.InvariantCulture)} {trend.CanonicalUnit}"
                : string.Empty;

            points.Add(new ChartPointItem(
                Label: xLabel,
                Value: p.NumericValue,
                IsOutOfNormalRange: p.IsOutOfNormalRange,
                IsCriticalPanic: p.IsCriticalPanic,
                AnnotationText: flagLabel,
                FullTimestampText: fullTimestamp,
                DetailText: $"{phaseLabel} · {flagLabel}{deltaPart}"));
        }

        return points;
    }

    private void ExecuteSaveLabResult()
    {
        if (_currentPatientId is null)
        {
            IsLabSaveCritical = false;
            IsLabSaveError = true;
            LabSaveStatusMessage = "Select a patient before recording a laboratory result.";
            return;
        }

        string loinc = (SelectedAddLabCatalog?.LoincCode ?? AddLabLoincCode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(loinc))
        {
            IsLabSaveCritical = false;
            IsLabSaveError = true;
            LabSaveStatusMessage = "Select a valid analyte before saving.";
            return;
        }

        if (!double.TryParse(AddLabValue?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double numericValue) ||
            !double.IsFinite(numericValue))
        {
            IsLabSaveCritical = false;
            IsLabSaveError = true;
            LabSaveStatusMessage = "Validation error: Enter a valid numeric lab value.";
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var catalogRepo = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();
            var labRepo = scope.ServiceProvider.GetRequiredService<ILabRepository>();
            var patientService = scope.ServiceProvider.GetRequiredService<IPatientService>();

            var catalogItem = catalogRepo.GetAllLoincLabDefinitions()
                .FirstOrDefault(c => string.Equals(c.LoincCode, loinc, StringComparison.OrdinalIgnoreCase));
            if (catalogItem is null)
            {
                IsLabSaveCritical = false;
                IsLabSaveError = true;
                LabSaveStatusMessage = $"Unknown analyte code '{loinc}'.";
                return;
            }

            var patientSummary = patientService.GetPatientClinicalSummary(_currentPatientId);
            if (patientSummary is null || string.IsNullOrWhiteSpace(patientSummary.AttendingDoctorId))
            {
                IsLabSaveCritical = false;
                IsLabSaveError = true;
                LabSaveStatusMessage = "Validation error: Attending physician record is missing for the selected patient.";
                return;
            }

            string doctorId = patientSummary.AttendingDoctorId;

            DateTime nowUtc = DateTime.UtcNow;
            DateTime monotonicUtc = nowUtc > _lastLabTimestampUtc
                ? nowUtc
                : _lastLabTimestampUtc.AddMilliseconds(25);
            _lastLabTimestampUtc = monotonicUtc;

            string utcIso = monotonicUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            string orderId = $"LAB-ORD-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}";
            string resultId = $"LAB-RES-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}";
            string phase = string.IsNullOrWhiteSpace(AddLabPhase) ? "Pre-Dialysis" : AddLabPhase.Trim();
            string flag = ClassifyAbnormalityFlag(numericValue, catalogItem);

            labRepo.InsertLabOrder(new LabOrderRecord(
                LabOrderId: orderId,
                PatientId: _currentPatientId,
                SessionId: null,
                OrderedByDoctorId: doctorId,
                SpecimenCollectionUtc: utcIso,
                SamplingPhase: phase,
                LabPanelName: $"{catalogItem.AnalyteName} Entry",
                OrderStatus: "Resulted"));

            labRepo.InsertLabResult(new LabResultRecord(
                LabResultId: resultId,
                LabOrderId: orderId,
                PatientId: _currentPatientId,
                LoincCode: catalogItem.LoincCode,
                NumericValue: numericValue,
                AbnormalityFlag: flag,
                VerifiedByLabTech: 1,
                ResultedAtUtc: utcIso));

            string savedPatientId = _currentPatientId;
            AddLabValue = string.Empty;
            ReloadLabTrends(scope);

            var matchingTrend = LabTrends.FirstOrDefault(t =>
                string.Equals(t.LoincCode, catalogItem.LoincCode, StringComparison.OrdinalIgnoreCase));
            if (matchingTrend is not null)
            {
                SelectedLabTrend = matchingTrend;
            }

            IsLabSaveError = false;
            string normalizedFlag = flag.Replace("_", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
            string humanFlag = ClinicalDisplayFormatter.FormatAbnormalityFlag(flag);
            if (normalizedFlag is "CRITICALHIGH" or "CRITICALLOW")
            {
                IsLabSaveCritical = true;
                LabSaveStatusMessage =
                    $"✓ Saved {catalogItem.AnalyteName} {FormatIsmpValue(numericValue, catalogItem.CanonicalUnit)} — {humanFlag} Alert";
            }
            else if (normalizedFlag is "HIGH" or "LOW")
            {
                IsLabSaveCritical = true;
                LabSaveStatusMessage =
                    $"✓ Saved {catalogItem.AnalyteName} {FormatIsmpValue(numericValue, catalogItem.CanonicalUnit)} — {humanFlag}";
            }
            else
            {
                IsLabSaveCritical = false;
                LabSaveStatusMessage =
                    $"✓ Saved {catalogItem.AnalyteName} {FormatIsmpValue(numericValue, catalogItem.CanonicalUnit)} — Normal Range";
            }

            _onClinicalDataUpdated?.Invoke(savedPatientId);
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or ArgumentException)
        {
            IsLabSaveCritical = false;
            IsLabSaveError = true;
            LabSaveStatusMessage = $"Unable to save laboratory result: {ex.Message}";
        }
    }

    private void ExecuteSelectBiomarker(object? param)
    {
        string code = param?.ToString()?.Trim() ?? "ALL";
        if (string.IsNullOrWhiteSpace(code))
        {
            code = "ALL";
        }

        if (!string.Equals(code, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            AddLabLoincCode = code;
        }

        SelectedLoincFilter = code;
    }

    private void NotifyBiomarkerPillStates()
    {
        OnPropertyChanged(nameof(IsAllLabsPillActive));
        OnPropertyChanged(nameof(IsPotassiumPillActive));
        OnPropertyChanged(nameof(IsHemoglobinPillActive));
        OnPropertyChanged(nameof(IsPhosphorusPillActive));
        OnPropertyChanged(nameof(IsPthPillActive));
    }

    private void ExecuteSetSessionWindow(object? param)
    {
        if (param is int intVal)
        {
            SelectedSessionWindowCount = intVal;
            return;
        }

        if (param is string strVal && int.TryParse(strVal, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            SelectedSessionWindowCount = parsed;
        }
    }

    private void ReloadSessionSeries(IServiceScope? existingScope = null)
    {
        if (_currentPatientId is null)
        {
            SessionSeriesSummary = null;
            SessionRows.Clear();
            OnPropertyChanged(nameof(HasSessionRows));
            SessionPreSbpPoints = Array.Empty<ChartPointItem>();
            SessionNadirSbpPoints = Array.Empty<ChartPointItem>();
            SessionAvgIdwgText = "--";
            SessionAvgPreSbpText = "--";
            SessionAvgPostSbpText = "--";
            SessionAvgUfText = "--";
            SessionLowestNadirText = "--";
            SessionLatestKtVText = "--";
            return;
        }

        using var ownedScope = existingScope is null ? _scopeFactory.CreateScope() : null;
        var sp = (existingScope ?? ownedScope)!.ServiceProvider;
        var sessionAnalyzer = sp.GetRequiredService<ISessionHistoryAnalyzer>();

        var summary = sessionAnalyzer.GetRecentSessionsSeries(_currentPatientId, SelectedSessionWindowCount);
        SessionSeriesSummary = summary;

        SessionRows.Clear();
        var prePoints = new List<ChartPointItem>(summary.Sessions.Count);
        var nadirPoints = new List<ChartPointItem>(summary.Sessions.Count);

        var dateCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var dateShiftCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in summary.Sessions)
        {
            dateCounts[s.SessionDate] = dateCounts.TryGetValue(s.SessionDate, out int dc) ? dc + 1 : 1;
            string dsKey = $"{s.SessionDate}|{s.ShiftNumber}";
            dateShiftCounts[dsKey] = dateShiftCounts.TryGetValue(dsKey, out int dsc) ? dsc + 1 : 1;
        }

        var dateShiftOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in summary.Sessions)
        {
            SessionRows.Add(s);

            string shortDate = s.SessionDate.Length >= 10 ? s.SessionDate[5..10] : s.SessionDate;
            string dsKey = $"{s.SessionDate}|{s.ShiftNumber}";

            string xLabel;
            if (dateCounts[s.SessionDate] <= 1)
            {
                xLabel = shortDate;
            }
            else if (dateShiftCounts[dsKey] <= 1)
            {
                xLabel = $"{shortDate} S{s.ShiftNumber}";
            }
            else
            {
                int ord = dateShiftOrdinals.TryGetValue(dsKey, out int cur) ? cur + 1 : 1;
                dateShiftOrdinals[dsKey] = ord;
                xLabel = $"{shortDate} #{ord}";
            }

            string fullTs = $"{s.SessionDate} · Shift {s.ShiftNumber}";
            string postStr = s.PostSystolicBp.HasValue ? $"{s.PostSystolicBp.Value} mmHg" : "--";
            string ktvStr = s.CalculatedSpKtV.HasValue
                ? s.CalculatedSpKtV.Value.ToString("0.##", CultureInfo.InvariantCulture)
                : "--";
            string ufrStr = ClinicalDisplayFormatter.FormatRateMlHr(s.EffectiveUfRateMlHr);

            prePoints.Add(new ChartPointItem(
                Label: xLabel,
                Value: s.PreSystolicBp,
                IsOutOfNormalRange: s.PreSystolicBp >= 160 || s.PreSystolicBp < 100,
                IsCriticalPanic: s.PreSystolicBp < 90,
                AnnotationText: s.PreSystolicBp < 90 ? "Hypotension" : s.PreSystolicBp >= 160 ? "Hypertensive" : null,
                FullTimestampText: fullTs,
                DetailText: $"Post BP {postStr} · UFR {ufrStr} · spKt/V {ktvStr}"));

            double? nadir = s.NadirSystolicBp ?? s.PostSystolicBp;
            if (nadir.HasValue)
            {
                nadirPoints.Add(new ChartPointItem(
                    Label: xLabel,
                    Value: nadir.Value,
                    IsOutOfNormalRange: nadir.Value < 100.0,
                    IsCriticalPanic: nadir.Value < 90.0,
                    AnnotationText: nadir.Value < 90.0 ? "< 90 mmHg" : nadir.Value < 100.0 ? "Borderline" : null,
                    FullTimestampText: fullTs,
                    DetailText: $"Pre BP {s.PreSystolicBp} mmHg · Incidents {s.IncidentCount} · spKt/V {ktvStr}"));
            }
        }

        SessionPreSbpPoints = prePoints;
        SessionNadirSbpPoints = nadirPoints;
        OnPropertyChanged(nameof(HasSessionRows));

        if (summary.ReturnedCount == 0)
        {
            SessionEmptyStateMessage = "No completed dialysis sessions recorded yet for this patient.";
            SessionAvgIdwgText = "--";
            SessionAvgPreSbpText = "--";
            SessionAvgPostSbpText = "--";
            SessionAvgUfText = "--";
            SessionLowestNadirText = "--";
            SessionLatestKtVText = "--";
            LowestNadirSeverityLevel = "Normal";
            LatestKtVSeverityLevel = "Normal";
        }
        else
        {
            string idwgSign = summary.AverageIdwgKg < 0 ? "-" : string.Empty;
            SessionAvgIdwgText = $"{idwgSign}{IsmpMedicationFormatter.FormatNumericDose(Math.Abs(summary.AverageIdwgKg), "kg")}";
            SessionAvgPreSbpText = $"{summary.AveragePreSystolicBp.ToString("0.#", CultureInfo.InvariantCulture)} mmHg";

            var postSbpValues = summary.Sessions
                .Where(s => s.PostSystolicBp.HasValue)
                .Select(s => (double)s.PostSystolicBp!.Value)
                .ToList();
            SessionAvgPostSbpText = postSbpValues.Count > 0
                ? $"{Math.Round(postSbpValues.Average(), 0).ToString("0", CultureInfo.InvariantCulture)} mmHg"
                : "--";

            var ufRates = summary.Sessions
                .Where(s => double.IsFinite(s.EffectiveUfRateMlHr) && s.EffectiveUfRateMlHr > 0.0)
                .Select(s => s.EffectiveUfRateMlHr)
                .ToList();
            SessionAvgUfText = ufRates.Count > 0
                ? ClinicalDisplayFormatter.FormatRateMlHr(ufRates.Average())
                : "--";

            SessionLowestNadirText = summary.LowestNadirSystolicBp.HasValue
                ? $"{summary.LowestNadirSystolicBp.Value.ToString("0.#", CultureInfo.InvariantCulture)} mmHg"
                : "--";
            SessionLatestKtVText = summary.LatestCalculatedSpKtV.HasValue
                ? summary.LatestCalculatedSpKtV.Value.ToString("0.##", CultureInfo.InvariantCulture)
                : "--";

            LowestNadirSeverityLevel = summary.LowestNadirSystolicBp switch
            {
                double nadir when nadir < 90.0 => "Critical",
                double nadir when nadir < 100.0 => "Borderline",
                _ => "Normal"
            };
            LatestKtVSeverityLevel = summary.LatestCalculatedSpKtV is double ktv && ktv < 1.20
                ? "Low"
                : "Normal";
        }
    }

    private void ReloadDryWeightTimeline(IServiceScope? existingScope = null)
    {
        DryWeightHistory.Clear();
        if (_currentPatientId is null)
        {
            OnPropertyChanged(nameof(HasDryWeightHistory));
            return;
        }

        using var ownedScope = existingScope is null ? _scopeFactory.CreateScope() : null;
        var sp = (existingScope ?? ownedScope)!.ServiceProvider;
        var patientService = sp.GetRequiredService<IPatientService>();

        var timeline = patientService.GetDryWeightHistory(_currentPatientId);
        foreach (var entry in timeline.OrderByDescending(x => x.DryWeightHistoryId))
        {
            DryWeightHistory.Add(entry);
        }

        OnPropertyChanged(nameof(HasDryWeightHistory));
    }

    private void ExecuteComputeKtV()
    {
        if (!TryParseInvariant(KtVPreBun, out double preBun) ||
            !TryParseInvariant(KtVPostBun, out double postBun) ||
            !TryParseInvariant(KtVDurationHours, out double durationHrs) ||
            !TryParseInvariant(KtVUfLiters, out double ufLiters) ||
            !TryParseInvariant(KtVPostWeightKg, out double postWtKg))
        {
            var reject = new DaugirdasKtVResult(
                IsValid: false,
                ValidationError: "All 5 numeric inputs (Pre-BUN, Post-BUN, Duration, UF Volume, Post-Weight) are required.",
                SpKtV: 0.0,
                UreaReductionRatioPercent: 0.0,
                IsAdequateKdoqiTarget: false,
                CitationLabel: DaugirdasKtVCalculator.AccreditedCitationLabel,
                ClinicalSummary: "Calculation rejected: enter valid numeric inputs for all 5 parameters.");

            KtVResult = reject;
            KtVErrorMessage = reject.ValidationError;
            KtVSpKtVFormatted = "--";
            KtVUrrFormatted = "--";
            KtVStatusBadgeText = "INVALID INPUT";
            KtVFormattedInputsSummary = string.Empty;
            KtVIsInadequateWarning = false;
            return;
        }

        var result = DaugirdasKtVCalculator.Calculate(new DaugirdasKtVInput(
            PreDialysisBunMgDl: preBun,
            PostDialysisBunMgDl: postBun,
            SessionDurationHours: durationHrs,
            UltrafiltrationVolumeLiters: ufLiters,
            PostDialysisWeightKg: postWtKg));

        KtVResult = result;
        if (!result.IsValid)
        {
            KtVErrorMessage = result.ValidationError;
            KtVSpKtVFormatted = "--";
            KtVUrrFormatted = "--";
            KtVStatusBadgeText = "OUT OF RANGE";
            KtVFormattedInputsSummary = string.Empty;
            KtVIsInadequateWarning = false;
            return;
        }

        KtVErrorMessage = null;
        KtVSpKtVFormatted = result.SpKtV.ToString("0.###", CultureInfo.InvariantCulture);
        KtVUrrFormatted = $"{result.UreaReductionRatioPercent.ToString("0.#", CultureInfo.InvariantCulture)}%";
        KtVStatusBadgeText = result.IsAdequateKdoqiTarget
            ? "ADEQUATE (KDOQI >= 1.2)"
            : "INADEQUATE (< 1.2 TARGET)";
        KtVIsInadequateWarning = !result.IsAdequateKdoqiTarget;
        KtVFormattedInputsSummary =
            $"UF {IsmpMedicationFormatter.FormatNumericDose(ufLiters, "L")} | Post-Wt {IsmpMedicationFormatter.FormatNumericDose(postWtKg, "kg")} | Duration {IsmpMedicationFormatter.FormatNumericDose(durationHrs, "hr")}";
    }

    private static bool TryParseInvariant(string raw, out double value)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = 0.0;
            return false;
        }

        return double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string ClassifyAbnormalityFlag(double value, LoincLabCatalogItem catalog)
    {
        if (catalog.CriticalPanicHigh.HasValue && value >= catalog.CriticalPanicHigh.Value)
        {
            return "CriticalHigh";
        }
        if (catalog.CriticalPanicLow.HasValue && value <= catalog.CriticalPanicLow.Value)
        {
            return "CriticalLow";
        }
        if (value > catalog.NormalRangeHigh)
        {
            return "High";
        }
        if (value < catalog.NormalRangeLow)
        {
            return "Low";
        }
        return "Normal";
    }

    private static string FormatIsmpValue(double value, string unit)
    {
        if (value >= 0.0 && double.IsFinite(value))
        {
            return IsmpMedicationFormatter.FormatNumericDose(value, unit);
        }

        string safeUnit = IsmpMedicationFormatter.NormalizeUnit(unit);
        return $"{value.ToString("0.##", CultureInfo.InvariantCulture)} {safeUnit}";
    }
}
