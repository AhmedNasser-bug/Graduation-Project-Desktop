using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NephroEdge.Desktop.Infrastructure;
using Persistence_Unit.Entities;
using Persistence_Unit.Interfaces;
using Services_Unit.Analytics.Interfaces;
using Services_Unit.Core.Contracts;
using Services_Unit.Core.Formatting;
using Services_Unit.Core.Interfaces;
using Services_Unit.DecisionSupport.Contracts;
using Services_Unit.DecisionSupport.Interfaces;
using Services_Unit.DecisionSupport.SessionConfig.Contracts;
using Services_Unit.DecisionSupport.SessionConfig.Interfaces;
using Services_Unit.RiskPrediction.Contracts;
using Services_Unit.RiskPrediction.Interfaces;

namespace NephroEdge.Desktop.ViewModels;

/// <summary>
/// ViewModel for Clinical Safety, ADE, Prescription Orders, and FDA Criterion 4 Explainability.
/// Enforces ECRI zero-autopopulate inputs, ISMP safe medication typography, FDA Criterion 4 split
/// deterministic vs qualitative explainability, and nurse OrderHandshake dispatch.
/// </summary>
public sealed class SafetyRxWorkbenchViewModel : ObservableObject
{
    private static readonly ConcurrentDictionary<string, string> RxNormDisplayLookup = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Regex HyphenAndUnitTrailingZeroRegex = new(
        @"(\b\d+)\.0(?=\s*(?:[-–/]|mmHg|mL/kg/h|mL/h|mL/min|g/dL|bpm|kg|L|mL|mg|mcg|units|mEq/L|m2|min|hr|h)\b)",
        RegexOptions.Compiled);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Action<string>? _onPatientRecordUpdated;

    private string? _currentPatientId;
    private SessionConfigPatientInput? _lastSessionConfigInput;
    private SessionConfigEvaluationResult? _lastSessionConfigResult;
    private double _lastSpKtV = 1.40;

    private bool _hasPatientSelected;
    private string _patientHeaderLabel = "Select a patient to open Clinical Safety & Prescription Orders.";
    private string _formattedCurrentDryWeight = "--";
    private string _formattedActiveHeparinSummary = "No active prescription loaded.";
    private string _formattedActiveBathSummary = "--";
    private string _formattedSampleTallManAlert = string.Empty;
    private string _formattedTotalBodyWater = "--";
    private string _formattedTitrationSummary = "Awaiting Hemoglobin (LOINC 718-7) lab entry — zero simulated titration.";

    // Criterion 4 & Decision Support State
    private IntradialyticRiskAssessmentResult? _riskAssessment;
    private ComprehensiveDecisionSupportResult? _decisionSupportResult;
    private string _overallSafetyBadgeText = "PENDING";
    private string _overallSafetyDisplayText = "Pending Evaluation";
    private bool _isSafetyApproved;
    private bool _isSafetyAdvisory;
    private bool _isSafetyBlocked;
    private bool _isSafetyPending = true;
    private string _riskSummaryHeadline = "Awaiting patient selection.";
    private string _algorithmPlainSummary = string.Empty;
    private string _telemetryVectorSummary = string.Empty;
    private string _requiredInputsSummary = string.Empty;
    private string _intendedUseSummary = string.Empty;
    private string _clinicianVerificationPrompt = string.Empty;
    private string _dataCompletenessText = "--";

    // Optional ADE Check Inputs (Zero Fake Data)
    private string _candidateConcomitantRxCui = string.Empty;
    private string _estimatedCrClText = string.Empty;

    // ECRI Zero-Autopopulate Dry Weight Update Inputs
    private string _newDryWeightKgText = string.Empty;
    private string _dryWeightRationaleText = string.Empty;
    private string _dryWeightStatusMessage = string.Empty;
    private bool _isDryWeightError;

    // ECRI Zero-Autopopulate Prescription Inputs
    private string _dialyzerModel = string.Empty;
    private string _surfaceAreaM2Text = string.Empty;
    private string _durationMinText = string.Empty;
    private string _qbText = string.Empty;
    private string _qdText = string.Empty;
    private string _potassiumMeqLText = string.Empty;
    private string _calciumMeqLText = string.Empty;
    private string _sodiumMeqLText = string.Empty;
    private string _bicarbonateMeqLText = string.Empty;
    private string _anticoagulantRxNormCui = string.Empty;
    private RxNormMedicationCatalogItem? _selectedAnticoagulantCatalog;
    private string _heparinBolusText = string.Empty;
    private string _heparinHourlyText = string.Empty;
    private string _prescriptionRationaleText = string.Empty;
    private string _rxStatusMessage = string.Empty;
    private bool _isRxError;

    // FHIR R4 Bundle Drawer
    private string _fhirBundleJsonText = string.Empty;
    private bool _isFhirDrawerExpanded;

    public SafetyRxWorkbenchViewModel(
        IServiceScopeFactory scopeFactory,
        Action<string>? onPatientRecordUpdated = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _onPatientRecordUpdated = onPatientRecordUpdated;

        SaveDryWeightCommand = new RelayCommand(ExecuteSaveDryWeight);
        SignPrescriptionCommand = new RelayCommand(ExecuteSignPrescription);
        FillStandardDefaultsCommand = new RelayCommand(ExecuteFillStandardDefaults);
        EvaluateSafetyCommand = new RelayCommand(ExecuteEvaluateSafety);
        ExportFhirBundleCommand = new RelayCommand(ExecuteExportFhirBundle);
        ToggleFhirDrawerCommand = new RelayCommand(() => IsFhirDrawerExpanded = !IsFhirDrawerExpanded);

        FormattedSampleTallManAlert = BuildIsmpSampleTallManBanner();
        LoadRxNormCatalog();
    }

    public static string FormatRxNormDisplay(string? rxCui)
    {
        if (string.IsNullOrWhiteSpace(rxCui))
        {
            return "Unspecified";
        }

        string trimmed = rxCui.Trim();
        if (RxNormDisplayLookup.TryGetValue(trimmed, out string? name))
        {
            return $"{IsmpMedicationFormatter.FormatTallManName(name)} ({trimmed})";
        }

        return $"RxNorm {trimmed}";
    }

    public static string FormatInteractingEntity(string? entity)
    {
        if (string.IsNullOrWhiteSpace(entity))
        {
            return "Unspecified";
        }

        string trimmed = entity.Trim();
        if (trimmed.StartsWith("OrganClearance:", StringComparison.OrdinalIgnoreCase))
        {
            string rest = trimmed["OrganClearance:".Length..].Trim();
            return SanitizeIsmpText($"Renal Clearance ({rest})");
        }

        if (RxNormDisplayLookup.TryGetValue(trimmed, out string? name))
        {
            return $"{IsmpMedicationFormatter.FormatTallManName(name)} ({trimmed})";
        }

        return SanitizeIsmpText(trimmed.Replace('_', ' '));
    }

    public static string FormatCleanRouteDoseText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        string cleanRoute = raw
            .Replace("IV_Circuit", ClinicalDisplayFormatter.FormatRoute("IV_Circuit"), StringComparison.Ordinal)
            .Replace("IV_Bolus", ClinicalDisplayFormatter.FormatRoute("IV_Bolus"), StringComparison.Ordinal);
        return SanitizeIsmpText(cleanRoute);
    }

    public static string SanitizeIsmpText(string? text)
    {
        string formatted = ClinicalDisplayFormatter.FormatCleanMeasurementText(text);
        if (string.IsNullOrEmpty(formatted))
        {
            return string.Empty;
        }

        return HyphenAndUnitTrailingZeroRegex.Replace(formatted, "$1");
    }

    public ObservableCollection<RiskDriverAttributionItem> Top3Drivers { get; } = new();

    public ObservableCollection<string> VerifiedDeterministicCitations { get; } = new();

    public ObservableCollection<string> QualitativeClinicalAdvisories { get; } = new();

    public ObservableCollection<DrugConflictRecord> Conflicts { get; } = new();

    public ObservableCollection<OrderHandshakeRecord> OrderHandshakes { get; } = new();

    public ObservableCollection<RxNormMedicationCatalogItem> RxNormCatalogItems { get; } = new();

    public RelayCommand SaveDryWeightCommand { get; }

    public RelayCommand SignPrescriptionCommand { get; }

    public RelayCommand FillStandardDefaultsCommand { get; }

    public RelayCommand EvaluateSafetyCommand { get; }

    public RelayCommand ExportFhirBundleCommand { get; }

    public RelayCommand ToggleFhirDrawerCommand { get; }

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

    public string FormattedCurrentDryWeight
    {
        get => _formattedCurrentDryWeight;
        private set => SetProperty(ref _formattedCurrentDryWeight, value);
    }

    public string FormattedActiveHeparinSummary
    {
        get => _formattedActiveHeparinSummary;
        private set => SetProperty(ref _formattedActiveHeparinSummary, value);
    }

    public string FormattedActiveBathSummary
    {
        get => _formattedActiveBathSummary;
        private set => SetProperty(ref _formattedActiveBathSummary, value);
    }

    public string FormattedSampleTallManAlert
    {
        get => _formattedSampleTallManAlert;
        private set => SetProperty(ref _formattedSampleTallManAlert, value);
    }

    public string FormattedTotalBodyWater
    {
        get => _formattedTotalBodyWater;
        private set => SetProperty(ref _formattedTotalBodyWater, value);
    }

    public string FormattedTitrationSummary
    {
        get => _formattedTitrationSummary;
        private set => SetProperty(ref _formattedTitrationSummary, value);
    }

    public IntradialyticRiskAssessmentResult? RiskAssessment
    {
        get => _riskAssessment;
        private set => SetProperty(ref _riskAssessment, value);
    }

    public ComprehensiveDecisionSupportResult? DecisionSupportResult
    {
        get => _decisionSupportResult;
        private set => SetProperty(ref _decisionSupportResult, value);
    }

    /// <summary>
    /// Raw enum string preserved for test/telemetry verification. UI pills bind to <see cref="OverallSafetyDisplayText"/>.
    /// </summary>
    public string OverallSafetyBadgeText
    {
        get => _overallSafetyBadgeText;
        private set => SetProperty(ref _overallSafetyBadgeText, value);
    }

    public string OverallSafetyDisplayText
    {
        get => _overallSafetyDisplayText;
        private set => SetProperty(ref _overallSafetyDisplayText, value);
    }

    public bool IsSafetyApproved
    {
        get => _isSafetyApproved;
        private set => SetProperty(ref _isSafetyApproved, value);
    }

    public bool IsSafetyAdvisory
    {
        get => _isSafetyAdvisory;
        private set => SetProperty(ref _isSafetyAdvisory, value);
    }

    public bool IsSafetyBlocked
    {
        get => _isSafetyBlocked;
        private set => SetProperty(ref _isSafetyBlocked, value);
    }

    public bool IsSafetyPending
    {
        get => _isSafetyPending;
        private set => SetProperty(ref _isSafetyPending, value);
    }

    public bool HasAdeConflicts => Conflicts.Count > 0;

    public string AdeSafetySummaryPillText
    {
        get
        {
            if (!HasPatientSelected || IsSafetyPending)
            {
                return "ADE Check Pending";
            }

            if (Conflicts.Count > 0)
            {
                return Conflicts.Count == 1 ? "1 Drug Conflict" : $"{Conflicts.Count} Drug Conflicts";
            }

            if (IsSafetyBlocked)
            {
                return "Review Required";
            }

            if (IsSafetyAdvisory)
            {
                return "Advisory Active";
            }

            return "ADE Clear";
        }
    }

    public string RiskSummaryHeadline
    {
        get => _riskSummaryHeadline;
        private set => SetProperty(ref _riskSummaryHeadline, value);
    }

    public string AlgorithmPlainSummary
    {
        get => _algorithmPlainSummary;
        private set => SetProperty(ref _algorithmPlainSummary, value);
    }

    public string TelemetryVectorSummary
    {
        get => _telemetryVectorSummary;
        private set => SetProperty(ref _telemetryVectorSummary, value);
    }

    public string RequiredInputsSummary
    {
        get => _requiredInputsSummary;
        private set => SetProperty(ref _requiredInputsSummary, value);
    }

    public string IntendedUseSummary
    {
        get => _intendedUseSummary;
        private set => SetProperty(ref _intendedUseSummary, value);
    }

    public string ClinicianVerificationPrompt
    {
        get => _clinicianVerificationPrompt;
        private set => SetProperty(ref _clinicianVerificationPrompt, value);
    }

    public string DataCompletenessText
    {
        get => _dataCompletenessText;
        private set => SetProperty(ref _dataCompletenessText, value);
    }

    public string CandidateConcomitantRxCui
    {
        get => _candidateConcomitantRxCui;
        set => SetProperty(ref _candidateConcomitantRxCui, value?.Trim() ?? string.Empty);
    }

    public string EstimatedCrClText
    {
        get => _estimatedCrClText;
        set => SetProperty(ref _estimatedCrClText, value ?? string.Empty);
    }

    public string NewDryWeightKgText
    {
        get => _newDryWeightKgText;
        set => SetProperty(ref _newDryWeightKgText, value ?? string.Empty);
    }

    public string DryWeightRationaleText
    {
        get => _dryWeightRationaleText;
        set => SetProperty(ref _dryWeightRationaleText, value ?? string.Empty);
    }

    public string DryWeightStatusMessage
    {
        get => _dryWeightStatusMessage;
        private set => SetProperty(ref _dryWeightStatusMessage, value);
    }

    public bool IsDryWeightError
    {
        get => _isDryWeightError;
        private set => SetProperty(ref _isDryWeightError, value);
    }

    public string DialyzerModel
    {
        get => _dialyzerModel;
        set => SetProperty(ref _dialyzerModel, value ?? string.Empty);
    }

    public string SurfaceAreaM2Text
    {
        get => _surfaceAreaM2Text;
        set => SetProperty(ref _surfaceAreaM2Text, value ?? string.Empty);
    }

    public string DurationMinText
    {
        get => _durationMinText;
        set => SetProperty(ref _durationMinText, value ?? string.Empty);
    }

    public string QbText
    {
        get => _qbText;
        set => SetProperty(ref _qbText, value ?? string.Empty);
    }

    public string QdText
    {
        get => _qdText;
        set => SetProperty(ref _qdText, value ?? string.Empty);
    }

    public string PotassiumMeqLText
    {
        get => _potassiumMeqLText;
        set => SetProperty(ref _potassiumMeqLText, value ?? string.Empty);
    }

    public string CalciumMeqLText
    {
        get => _calciumMeqLText;
        set => SetProperty(ref _calciumMeqLText, value ?? string.Empty);
    }

    public string SodiumMeqLText
    {
        get => _sodiumMeqLText;
        set => SetProperty(ref _sodiumMeqLText, value ?? string.Empty);
    }

    public string BicarbonateMeqLText
    {
        get => _bicarbonateMeqLText;
        set => SetProperty(ref _bicarbonateMeqLText, value ?? string.Empty);
    }

    public string AnticoagulantRxNormCui
    {
        get => _anticoagulantRxNormCui;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (SetProperty(ref _anticoagulantRxNormCui, normalized))
            {
                SyncAnticoagulantFromCui(normalized);
            }
        }
    }

    public RxNormMedicationCatalogItem? SelectedAnticoagulantCatalog
    {
        get => _selectedAnticoagulantCatalog;
        set
        {
            if (SetProperty(ref _selectedAnticoagulantCatalog, value))
            {
                SetProperty(ref _anticoagulantRxNormCui, value?.RxNormCui ?? string.Empty, nameof(AnticoagulantRxNormCui));
            }
        }
    }

    public string HeparinBolusText
    {
        get => _heparinBolusText;
        set => SetProperty(ref _heparinBolusText, value ?? string.Empty);
    }

    public string HeparinHourlyText
    {
        get => _heparinHourlyText;
        set => SetProperty(ref _heparinHourlyText, value ?? string.Empty);
    }

    public string PrescriptionRationaleText
    {
        get => _prescriptionRationaleText;
        set => SetProperty(ref _prescriptionRationaleText, value ?? string.Empty);
    }

    public string RxStatusMessage
    {
        get => _rxStatusMessage;
        private set => SetProperty(ref _rxStatusMessage, value);
    }

    public bool IsRxError
    {
        get => _isRxError;
        private set => SetProperty(ref _isRxError, value);
    }

    public string FhirBundleJsonText
    {
        get => _fhirBundleJsonText;
        private set => SetProperty(ref _fhirBundleJsonText, value);
    }

    public bool IsFhirDrawerExpanded
    {
        get => _isFhirDrawerExpanded;
        set
        {
            if (SetProperty(ref _isFhirDrawerExpanded, value))
            {
                OnPropertyChanged(nameof(FhirToggleButtonText));
            }
        }
    }

    public string FhirToggleButtonText => IsFhirDrawerExpanded ? "Hide FHIR JSON" : "Show FHIR JSON";

    public event EventHandler? PatientContextSwitched;

    public void LoadForPatient(string? patientId)
    {
        bool patientChanged = !string.Equals(_currentPatientId, patientId, StringComparison.Ordinal);
        _currentPatientId = string.IsNullOrWhiteSpace(patientId) ? null : patientId.Trim();

        if (patientChanged)
        {
            ClearOrderEntryAndStaleContext();
            PatientContextSwitched?.Invoke(this, EventArgs.Empty);
        }

        LoadRxNormCatalog();

        if (_currentPatientId is null)
        {
            HasPatientSelected = false;
            PatientHeaderLabel = "Select a patient to open Clinical Safety & Prescription Orders.";
            ClearEvaluatedOutputs();
            OrderHandshakes.Clear();
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var patientService = scope.ServiceProvider.GetRequiredService<IPatientService>();
        var handshakeService = scope.ServiceProvider.GetRequiredService<IOrderHandshakeService>();

        var summary = patientService.GetPatientClinicalSummary(_currentPatientId);
        if (summary is null)
        {
            HasPatientSelected = false;
            PatientHeaderLabel = $"Patient '{_currentPatientId}' not found.";
            ClearEvaluatedOutputs();
            OrderHandshakes.Clear();
            return;
        }

        HasPatientSelected = true;
        PatientHeaderLabel = $"{summary.FullName} ({summary.MedicalRecordNumber}) — Clinical Safety & Prescriptions";
        FormattedCurrentDryWeight = summary.CurrentDryWeightKg is double dw && dw > 0.0
            ? IsmpMedicationFormatter.FormatNumericDose(dw, "kg")
            : "--";

        var activeRx = patientService.GetActivePrescription(_currentPatientId);
        FormatActivePrescriptionStrings(activeRx);
        RefreshOrderHandshakes(handshakeService, _currentPatientId);
        EvaluateSafetyAndRisk(scope, summary, activeRx);
    }

    private void ClearOrderEntryAndStaleContext()
    {
        // ECRI zero autopopulate: explicit empty inputs on every patient change
        NewDryWeightKgText = string.Empty;
        DryWeightRationaleText = string.Empty;
        DryWeightStatusMessage = string.Empty;
        IsDryWeightError = false;

        DialyzerModel = string.Empty;
        SurfaceAreaM2Text = string.Empty;
        DurationMinText = string.Empty;
        QbText = string.Empty;
        QdText = string.Empty;
        PotassiumMeqLText = string.Empty;
        CalciumMeqLText = string.Empty;
        SodiumMeqLText = string.Empty;
        BicarbonateMeqLText = string.Empty;
        HeparinBolusText = string.Empty;
        HeparinHourlyText = string.Empty;
        PrescriptionRationaleText = string.Empty;
        AnticoagulantRxNormCui = string.Empty;
        SelectedAnticoagulantCatalog = null;
        RxStatusMessage = string.Empty;
        IsRxError = false;

        CandidateConcomitantRxCui = string.Empty;
        EstimatedCrClText = string.Empty;
        FhirBundleJsonText = string.Empty;
        IsFhirDrawerExpanded = false;
    }

    private void ClearEvaluatedOutputs()
    {
        FormattedCurrentDryWeight = "--";
        FormattedActiveHeparinSummary = "No active prescription loaded.";
        FormattedActiveBathSummary = "--";
        FormattedTotalBodyWater = "--";
        FormattedTitrationSummary = "Awaiting Hemoglobin (LOINC 718-7) lab entry — zero simulated titration.";

        RiskAssessment = null;
        DecisionSupportResult = null;
        OverallSafetyBadgeText = "PENDING";
        OverallSafetyDisplayText = "Pending Evaluation";
        IsSafetyApproved = false;
        IsSafetyAdvisory = false;
        IsSafetyBlocked = false;
        IsSafetyPending = true;

        RiskSummaryHeadline = "Awaiting patient selection.";
        AlgorithmPlainSummary = string.Empty;
        TelemetryVectorSummary = string.Empty;
        RequiredInputsSummary = string.Empty;
        IntendedUseSummary = string.Empty;
        ClinicianVerificationPrompt = string.Empty;
        DataCompletenessText = "--";

        Top3Drivers.Clear();
        VerifiedDeterministicCitations.Clear();
        QualitativeClinicalAdvisories.Clear();
        Conflicts.Clear();
        _lastSessionConfigInput = null;
        _lastSessionConfigResult = null;
        OnPropertyChanged(nameof(HasAdeConflicts));
        OnPropertyChanged(nameof(AdeSafetySummaryPillText));
    }

    private void LoadRxNormCatalog()
    {
        using var scope = _scopeFactory.CreateScope();
        var catalogRepo = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();
        var meds = catalogRepo.GetAllRxNormMedications();

        foreach (var med in meds)
        {
            RxNormDisplayLookup[med.RxNormCui] = med.GenericName;
        }

        if (RxNormCatalogItems.Count == 0 && meds.Count > 0)
        {
            foreach (var med in meds)
            {
                RxNormCatalogItems.Add(med);
            }

            SyncAnticoagulantFromCui(_anticoagulantRxNormCui);
        }
    }

    private void SyncAnticoagulantFromCui(string cui)
    {
        if (string.IsNullOrWhiteSpace(cui))
        {
            if (_selectedAnticoagulantCatalog is not null)
            {
                SetProperty(ref _selectedAnticoagulantCatalog, null, nameof(SelectedAnticoagulantCatalog));
            }

            return;
        }

        if (RxNormCatalogItems.Count == 0)
        {
            return;
        }

        var match = RxNormCatalogItems.FirstOrDefault(m =>
            string.Equals(m.RxNormCui, cui, StringComparison.OrdinalIgnoreCase));
        if (match is not null && !Equals(_selectedAnticoagulantCatalog, match))
        {
            SetProperty(ref _selectedAnticoagulantCatalog, match, nameof(SelectedAnticoagulantCatalog));
        }
    }

    private void FormatActivePrescriptionStrings(DialysisPrescriptionRecord? activeRx)
    {
        if (activeRx is null)
        {
            FormattedActiveHeparinSummary = "No active dialysis prescription on file.";
            FormattedActiveBathSummary = "No active dialysate bath or flow orders recorded.";
            return;
        }

        var med = RxNormCatalogItems.FirstOrDefault(m =>
            string.Equals(m.RxNormCui, activeRx.AnticoagulantRxNormCui, StringComparison.OrdinalIgnoreCase));
        string medName = med?.GenericName ?? $"RxNorm {activeRx.AnticoagulantRxNormCui}";
        string unit = med?.StandardUnit ?? "units";
        string route = ClinicalDisplayFormatter.FormatRoute(med?.DefaultRoute ?? "IV_Circuit");

        if (activeRx.AnticoagulantBolusUnits > 0.0 || activeRx.AnticoagulantHourlyUnits > 0.0)
        {
            string bolusLine = IsmpMedicationFormatter.FormatPrescriptionLine(
                medName, activeRx.AnticoagulantBolusUnits, unit, route);
            string hourlyDose = IsmpMedicationFormatter.FormatNumericDose(
                activeRx.AnticoagulantHourlyUnits, unit);
            FormattedActiveHeparinSummary = SanitizeIsmpText($"{bolusLine} bolus + {hourlyDose}/hr maintenance");
        }
        else
        {
            string safeDrug = IsmpMedicationFormatter.FormatTallManName(medName);
            FormattedActiveHeparinSummary = SanitizeIsmpText($"{safeDrug} 0 units {route} (Anticoagulation-free)");
        }

        string area = IsmpMedicationFormatter.FormatNumericDose(activeRx.SurfaceAreaM2, "m2");
        string duration = IsmpMedicationFormatter.FormatNumericDose(activeRx.TargetDurationMinutes, "min");
        string qb = IsmpMedicationFormatter.FormatNumericDose(activeRx.PrescribedBloodFlowMlMin, "mL/min");
        string qd = IsmpMedicationFormatter.FormatNumericDose(activeRx.PrescribedDialysateFlowMlMin, "mL/min");
        string k = SanitizeIsmpText(activeRx.DialysatePotassiumMeqL.ToString("0.##", CultureInfo.InvariantCulture));
        string ca = SanitizeIsmpText(activeRx.DialysateCalciumMeqL.ToString("0.##", CultureInfo.InvariantCulture));
        string na = SanitizeIsmpText(activeRx.DialysateSodiumMeqL.ToString("0.##", CultureInfo.InvariantCulture));
        string hco3 = SanitizeIsmpText(activeRx.DialysateBicarbonateMeqL.ToString("0.##", CultureInfo.InvariantCulture));

        FormattedActiveBathSummary = SanitizeIsmpText(
            $"Dialyzer {activeRx.DialyzerMembraneModel} ({area}) · {duration} · Qb {qb} · Qd {qd} · Bath K {k}, Ca {ca}, Na {na}, HCO3 {hco3} mEq/L");
    }

    private void RefreshOrderHandshakes(IOrderHandshakeService handshakeService, string patientId)
    {
        OrderHandshakes.Clear();
        var directives = handshakeService.GetDirectivesForPatient(patientId);
        foreach (var item in directives)
        {
            OrderHandshakes.Add(item);
        }
    }

    private void ExecuteEvaluateSafety()
    {
        if (_currentPatientId is null)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var patientService = scope.ServiceProvider.GetRequiredService<IPatientService>();
        var summary = patientService.GetPatientClinicalSummary(_currentPatientId);
        if (summary is null)
        {
            return;
        }

        var activeRx = patientService.GetActivePrescription(_currentPatientId);
        EvaluateSafetyAndRisk(scope, summary, activeRx);
    }

    private void EvaluateSafetyAndRisk(
        IServiceScope scope,
        PatientSummaryView summary,
        DialysisPrescriptionRecord? activeRx)
    {
        var sessionService = scope.ServiceProvider.GetRequiredService<ISessionService>();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
        var medicationRepo = scope.ServiceProvider.GetRequiredService<IMedicationRepository>();
        var labAnalyzer = scope.ServiceProvider.GetRequiredService<ILabTimeSeriesAnalyzer>();
        var riskModule = scope.ServiceProvider.GetRequiredService<IRiskPredictionModule>();
        var decisionModule = scope.ServiceProvider.GetRequiredService<IDecisionSupportModule>();

        var (activeBoard, latestCompleted, resolvedSessionId) = ResolveLatestSessionContext(
            sessionService, sessionRepo, summary.PatientId);

        IReadOnlyList<IntradialyticTelemetryRecord> telemetry = resolvedSessionId is not null
            ? sessionService.GetSessionTelemetry(resolvedSessionId)
            : Array.Empty<IntradialyticTelemetryRecord>();

        IReadOnlyList<SessionMedicationLogRecord> sessionMeds = resolvedSessionId is not null
            ? medicationRepo.GetMedicationsForSession(resolvedSessionId)
            : Array.Empty<SessionMedicationLogRecord>();

        var nadirLags = sessionRepo.GetRecentPatientSessionNadirSbp(summary.PatientId, 10);
        var recentHistory = sessionRepo.GetRecentCompletedSessionsForPatient(summary.PatientId, 10);

        double dryWeightKg = summary.CurrentDryWeightKg is double dw && dw > 0.0 ? dw : 70.0;
        double preWeightKg = activeBoard?.PreDialysisWeightKg
                             ?? latestCompleted?.PreDialysisWeightKg
                             ?? dryWeightKg;
        double preSbp = activeBoard?.PreSystolicBp ?? latestCompleted?.PreSystolicBp ?? 130.0;
        double preDbp = activeBoard?.PreDiastolicBp ?? latestCompleted?.PreDiastolicBp ?? 80.0;
        double preHr = activeBoard?.PreHeartRateBpm ?? 76.0;
        double targetUfMl = activeBoard?.TargetUltrafiltrationMl ?? latestCompleted?.TargetUltrafiltrationMl ?? 0.0;
        double durationHours = (activeRx?.TargetDurationMinutes ?? 240.0) / 60.0;
        int ageYears = (int)Math.Clamp(summary.CalculatedAgeYears, 18L, 110L);
        int sexCode = string.Equals(summary.BiologicalSex, "F", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
        double heightCm = summary.HeightCm > 0.0 ? summary.HeightCm : 170.0;

        var tickSamples = telemetry.Select(t => new IntradialyticTelemetryTickSample(
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

        var latestTick = telemetry.LastOrDefault();
        var hybridInput = new IntradialyticRiskScoringHybridInput(
            SessionId: resolvedSessionId ?? $"SYN-EVAL-{summary.PatientId}",
            PatientId: summary.PatientId,
            AgeYears: ageYears,
            SexCode: sexCode,
            TargetDryWeightKg: dryWeightKg,
            PreSessionWeightKg: preWeightKg,
            TargetUfMl: targetUfMl,
            PreSessionSbpMmHg: preSbp,
            PreSessionDbpMmHg: preDbp,
            PreSessionHrBpm: preHr,
            DialysateTempC: latestTick?.DialysateTemperatureCelsius ?? 36.5,
            DialysateConductivityMsCm: latestTick?.DialysateConductivityMsCm ?? 13.8,
            BloodFlowRateMlMin: activeRx?.PrescribedBloodFlowMlMin ?? 320.0,
            DialysateFlowRateMlMin: activeRx?.PrescribedDialysateFlowMlMin ?? 500.0,
            ArterialPressureMmHg: latestTick?.ArterialPressureMmHg ?? -140.0,
            VenousPressureMmHg: latestTick?.VenousPressureMmHg ?? 130.0,
            TmpMmHg: latestTick?.TransmembranePressureMmHg ?? 95.0,
            RawTelemetryWindowTicks: tickSamples,
            HistoricalSessionNadirSbpLags: nadirLags);

        // Local deterministic Tier-1 predictor runs instantaneously offline without remote HTTP timeout stall
        var riskResult = riskModule.EvaluateSessionRiskAsync(hybridInput, preferRemoteAi: false)
            .GetAwaiter().GetResult();
        RiskAssessment = riskResult;

        double historicalIdhRate = recentHistory.Count > 0
            ? Math.Round(100.0 * recentHistory.Count(s => s.IncidentCount > 0 || (s.NadirSystolicBp.HasValue && s.NadirSystolicBp.Value < 90.0)) / recentHistory.Count, 1)
            : 0.0;

        var sessionConfigInput = new SessionConfigPatientInput(
            PatientId: summary.PatientId,
            AgeYears: ageYears,
            SexCode: sexCode,
            HeightCm: heightCm,
            TargetDryWeightKg: dryWeightKg,
            PreDialysisWeightKg: preWeightKg,
            PreDialysisSystolicBp: preSbp,
            PreDialysisDiastolicBp: preDbp,
            VascularAccessType: summary.CurrentAccessType ?? "AV_Fistula",
            HistoricalIdhRatePercentage: historicalIdhRate,
            PrescribedDurationHours: durationHours);

        var candidateDrugs = new List<string> { activeRx?.AnticoagulantRxNormCui ?? "5224" };
        if (!string.IsNullOrWhiteSpace(CandidateConcomitantRxCui))
        {
            candidateDrugs.Add(CandidateConcomitantRxCui.Trim());
        }

        var activeDrugs = sessionMeds
            .Select(m => m.RxNormCui)
            .Concat(new[] { activeRx?.AnticoagulantRxNormCui ?? "5224" })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        double crCl = double.TryParse(EstimatedCrClText?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedCrCl) &&
                      double.IsFinite(parsedCrCl) && parsedCrCl >= 0.0
            ? parsedCrCl
            : 12.0;

        var labTrends = labAnalyzer.GetLabTrendsForPatient(summary.PatientId);
        var hbTrend = labTrends.FirstOrDefault(t => string.Equals(t.LoincCode, "718-7", StringComparison.Ordinal));
        var ferritinTrend = labTrends.FirstOrDefault(t => string.Equals(t.LoincCode, "2276-4", StringComparison.Ordinal));

        AnemiaTitrationInput? anemiaInput = hbTrend?.LatestValue is double hbVal
            ? new AnemiaTitrationInput(
                PatientId: summary.PatientId,
                CurrentBiomarkerLevel: hbVal,
                BiomarkerDelta2To4Weeks: hbTrend.LatestDelta ?? 0.0,
                TransferrinSaturationPercent: 25.0,
                FerritinNgMl: ferritinTrend?.LatestValue ?? 350.0,
                CurrentWeeklyDoseUnits: 4000.0,
                IsOnActiveIvIronCourse: false)
            : null;

        var safetyInput = new PrescriptionSafetyInput(
            PatientId: summary.PatientId,
            CandidateRxNormCodes: candidateDrugs,
            ActiveRxNormCodes: activeDrugs,
            EstimatedCrClMlMin: crCl,
            AnemiaInput: anemiaInput,
            SessionConfigInput: sessionConfigInput,
            PrescribedDialysatePotassiumMeqL: activeRx?.DialysatePotassiumMeqL ?? 2.0,
            PrescribedDialysateCalciumMeqL: activeRx?.DialysateCalciumMeqL ?? 1.25);

        var cdssResult = decisionModule.EvaluateComprehensivePrescriptionAsync(safetyInput)
            .GetAwaiter().GetResult();
        DecisionSupportResult = cdssResult;

        _lastSessionConfigInput = sessionConfigInput;
        _lastSessionConfigResult = cdssResult.SessionConfigResult;
        _lastSpKtV = latestCompleted?.CalculatedSpKtV is double ktv && ktv > 0.0 ? ktv : 1.40;

        PopulateCriterion4ViewState(riskResult, cdssResult);
    }

    private void PopulateCriterion4ViewState(
        IntradialyticRiskAssessmentResult risk,
        ComprehensiveDecisionSupportResult cdss)
    {
        OverallSafetyBadgeText = cdss.OverallStatus.ToString();
        OverallSafetyDisplayText = ClinicalDisplayFormatter.FormatCdssStatus(cdss.OverallStatus);
        IsSafetyApproved = cdss.OverallStatus == OverallSafetyStatus.Approved;
        IsSafetyAdvisory = cdss.OverallStatus == OverallSafetyStatus.WarningAdvisory;
        IsSafetyBlocked = cdss.OverallStatus == OverallSafetyStatus.BlockedContraindication;
        IsSafetyPending = false;

        string alarmLabel = ClinicalDisplayFormatter.FormatAlarmTier(risk.AlarmPriority);
        string sourceLabel = ClinicalDisplayFormatter.FormatExecutionSource(risk.ExecutionSource);
        RiskSummaryHeadline = SanitizeIsmpText(
            $"IDH Risk: {risk.RiskBand} ({(risk.IdhProbabilityScore * 100.0).ToString("0.#", CultureInfo.InvariantCulture)}%) · {alarmLabel} · {sourceLabel}");

        var disclosure = risk.TransparencyDisclosure;
        string rulesSummary = risk.TriggeredRuleIds.Count > 0
            ? string.Join(", ", risk.TriggeredRuleIds.Select(ClinicalDisplayFormatter.FormatRuleLabel))
            : "All physiological parameters within Flythe & KDOQI safe thresholds";
        AlgorithmPlainSummary = SanitizeIsmpText(
            $"Clinical rule engine assessed intradialytic stability ({risk.RiskBand} risk): {rulesSummary}.");

        RequiredInputsSummary = SanitizeIsmpText(disclosure.RequiredInputsAndQualityRules);
        IntendedUseSummary = SanitizeIsmpText(disclosure.IntendedUseAndPopulation);
        ClinicianVerificationPrompt = SanitizeIsmpText(disclosure.ClinicianVerificationPrompt);
        DataCompletenessText = $"{(disclosure.DataCompletenessRatio * 100.0).ToString("0", CultureInfo.InvariantCulture)}%";

        var fv = risk.FeatureVector;
        TelemetryVectorSummary = SanitizeIsmpText(
            $"Latest SBP {fv.LatestSbpMmHg.ToString("0.#", CultureInfo.InvariantCulture)} mmHg · " +
            $"SBP Drop -{fv.DeltaSbpFromPreBaselineMmHg.ToString("0.#", CultureInfo.InvariantCulture)} mmHg · " +
            $"Rate {fv.SbpLinearSlopeMmHgPerHour.ToString("0.#", CultureInfo.InvariantCulture)} mmHg/h · " +
            $"UFR {fv.EffectiveUfrMlPerKgPerHour.ToString("0.##", CultureInfo.InvariantCulture)} mL/kg/h · " +
            $"10-Session Nadir Min {fv.HistoricalNadirMinSbpMmHg.ToString("0.#", CultureInfo.InvariantCulture)} mmHg");

        FormattedTotalBodyWater = IsmpMedicationFormatter.FormatNumericDose(
            cdss.SessionConfigResult.TotalBodyWaterLiters, "L");

        if (cdss.TitrationGuidance is { } titration)
        {
            string recDose = IsmpMedicationFormatter.FormatNumericDose(titration.RecommendedDoseUnits, "units");
            FormattedTitrationSummary = SanitizeIsmpText(
                $"AnemiaCare HD: {FormatTitrationAction(titration.Action)} · Weekly ESA {recDose} (Re-eval {titration.ReEvaluationIntervalWeeks}w) — {titration.GuidelineCitation}");
        }
        else
        {
            FormattedTitrationSummary =
                "Awaiting verified Hemoglobin (LOINC 718-7) lab result — zero simulated titration values.";
        }

        Top3Drivers.Clear();
        foreach (var driver in risk.Top3Drivers)
        {
            Top3Drivers.Add(driver with
            {
                ObservedValueText = SanitizeIsmpText(driver.ObservedValueText),
                ReferenceThresholdText = SanitizeIsmpText(driver.ReferenceThresholdText)
            });
        }

        VerifiedDeterministicCitations.Clear();
        foreach (string ruleId in risk.TriggeredRuleIds)
        {
            VerifiedDeterministicCitations.Add(SanitizeIsmpText($"Triggered Check: {ClinicalDisplayFormatter.FormatRuleLabel(ruleId)}"));
        }
        foreach (string citation in disclosure.SourceGuidelineCitations)
        {
            VerifiedDeterministicCitations.Add(SanitizeIsmpText(citation));
        }
        foreach (string configCitation in cdss.SessionConfigResult.GuidelineCitations)
        {
            string cleanConfigCitation = SanitizeIsmpText(configCitation);
            if (!VerifiedDeterministicCitations.Contains(cleanConfigCitation))
            {
                VerifiedDeterministicCitations.Add(cleanConfigCitation);
            }
        }

        QualitativeClinicalAdvisories.Clear();
        QualitativeClinicalAdvisories.Add(SanitizeIsmpText($"Clinician Verification: {disclosure.ClinicianVerificationPrompt}"));
        foreach (string alert in cdss.SessionConfigResult.ClinicalAlerts)
        {
            QualitativeClinicalAdvisories.Add(SanitizeIsmpText($"Session Advisory: {HumanizeSessionAlert(alert)}"));
        }
        foreach (var conflict in cdss.Conflicts)
        {
            QualitativeClinicalAdvisories.Add(SanitizeIsmpText(
                $"ADE [{conflict.Severity}] {FormatRxNormDisplay(conflict.PrimaryRxCui)} vs {FormatInteractingEntity(conflict.InteractingEntity)}: {conflict.ClinicalGuidance}"));
        }
        if (cdss.TitrationGuidance is { } titr)
        {
            QualitativeClinicalAdvisories.Add(SanitizeIsmpText($"AnemiaCare HD Guidance: {titr.GuidelineCitation}"));
        }

        Conflicts.Clear();
        foreach (var conflict in cdss.Conflicts)
        {
            Conflicts.Add(conflict with
            {
                MechanismSummary = SanitizeIsmpText(conflict.MechanismSummary),
                ClinicalGuidance = SanitizeIsmpText(conflict.ClinicalGuidance)
            });
        }

        OnPropertyChanged(nameof(HasAdeConflicts));
        OnPropertyChanged(nameof(AdeSafetySummaryPillText));
    }

    private void ExecuteSaveDryWeight()
    {
        if (_currentPatientId is null)
        {
            IsDryWeightError = true;
            DryWeightStatusMessage = "Select a patient before updating Target Dry Weight.";
            return;
        }

        if (!double.TryParse(NewDryWeightKgText?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double newWeightKg) ||
            !double.IsFinite(newWeightKg) || newWeightKg < 20.0 || newWeightKg > 250.0)
        {
            IsDryWeightError = true;
            DryWeightStatusMessage = "Validation error: Enter an intentional dry weight between 20 and 250 kg.";
            return;
        }

        if (string.IsNullOrWhiteSpace(DryWeightRationaleText))
        {
            IsDryWeightError = true;
            DryWeightStatusMessage = "Validation error: Please enter a clinical rationale before saving Target Dry Weight.";
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var patientService = scope.ServiceProvider.GetRequiredService<IPatientService>();
            var summary = patientService.GetPatientClinicalSummary(_currentPatientId);
            if (summary is null || string.IsNullOrWhiteSpace(summary.AttendingDoctorId))
            {
                IsDryWeightError = true;
                DryWeightStatusMessage = "Validation error: Attending physician record is missing for the selected patient.";
                return;
            }

            var directive = patientService.UpdateDryWeightAndQueueDirective(new DryWeightUpdateRequest(
                PatientId: _currentPatientId,
                TargetDryWeightKg: newWeightKg,
                PrescribedByDoctorId: summary.AttendingDoctorId,
                ClinicalRationale: DryWeightRationaleText.Trim()));

            NewDryWeightKgText = string.Empty;
            DryWeightRationaleText = string.Empty;

            string updatedPatientId = _currentPatientId;
            LoadForPatient(updatedPatientId);
            _onPatientRecordUpdated?.Invoke(updatedPatientId);

            IsDryWeightError = false;
            DryWeightStatusMessage = SanitizeIsmpText(
                $"Target Dry Weight updated to {directive.IsmpFormattedDoseText} and sent to bedside nursing ({ClinicalDisplayFormatter.FormatHandshakeStatus(directive.Status)}).");
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or ArgumentException)
        {
            IsDryWeightError = true;
            DryWeightStatusMessage = $"Unable to save Target Dry Weight: {ex.Message}";
        }
    }

    private void ExecuteSignPrescription()
    {
        if (_currentPatientId is null)
        {
            IsRxError = true;
            RxStatusMessage = "Select a patient before signing a dialysis prescription.";
            return;
        }

        if (string.IsNullOrWhiteSpace(DialyzerModel))
        {
            IsRxError = true;
            RxStatusMessage = "Validation error: Please enter an explicit Dialyzer Model.";
            return;
        }

        if (!TryParseBounded(DurationMinText, 60.0, 480.0, out double durationMin))
        {
            IsRxError = true;
            RxStatusMessage = "Validation error: Duration must be between 60 and 480 min.";
            return;
        }

        if (!TryParseBounded(QbText, 50.0, 600.0, out double qb))
        {
            IsRxError = true;
            RxStatusMessage = "Validation error: Blood Flow (Qb) must be between 50 and 600 mL/min.";
            return;
        }

        if (!TryParseBounded(QdText, 200.0, 1000.0, out double qd))
        {
            IsRxError = true;
            RxStatusMessage = "Validation error: Dialysate Flow (Qd) must be between 200 and 1000 mL/min.";
            return;
        }

        if (!TryParseBounded(PotassiumMeqLText, 1.0, 4.0, out double potassium))
        {
            IsRxError = true;
            RxStatusMessage = "Validation error: Dialysate K+ must be between 1 and 4 mEq/L.";
            return;
        }

        if (!TryParseBounded(HeparinBolusText, 0.0, 50000.0, out double bolusUnits))
        {
            IsRxError = true;
            RxStatusMessage = "Validation error: Heparin Bolus must be a finite non-negative number (units).";
            return;
        }

        if (!TryParseBounded(HeparinHourlyText, 0.0, 20000.0, out double hourlyUnits))
        {
            IsRxError = true;
            RxStatusMessage = "Validation error: Heparin Hourly rate must be a finite non-negative number (units/hr).";
            return;
        }

        if (!TryParseBounded(SurfaceAreaM2Text, 0.5, 3.0, out double surfaceArea))
        {
            IsRxError = true;
            RxStatusMessage = "Validation error: Surface Area must be between 0.5 and 3 m2.";
            return;
        }

        if (!TryParseBounded(CalciumMeqLText, 1.0, 2.0, out double calcium))
        {
            IsRxError = true;
            RxStatusMessage = "Validation error: Dialysate Ca2+ must be between 1 and 2 mEq/L.";
            return;
        }

        if (!TryParseBounded(SodiumMeqLText, 130.0, 150.0, out double sodium))
        {
            IsRxError = true;
            RxStatusMessage = "Validation error: Dialysate Na+ must be between 130 and 150 mEq/L.";
            return;
        }

        if (!TryParseBounded(BicarbonateMeqLText, 25.0, 40.0, out double bicarbonate))
        {
            IsRxError = true;
            RxStatusMessage = "Validation error: Dialysate HCO3- must be between 25 and 40 mEq/L.";
            return;
        }

        string anticoagulantCui = !string.IsNullOrWhiteSpace(SelectedAnticoagulantCatalog?.RxNormCui)
            ? SelectedAnticoagulantCatalog.RxNormCui.Trim()
            : AnticoagulantRxNormCui.Trim();
        if (string.IsNullOrWhiteSpace(anticoagulantCui))
        {
            IsRxError = true;
            RxStatusMessage = "Validation error: Please select an anticoagulant (or click Fill Standard Defaults).";
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var patientService = scope.ServiceProvider.GetRequiredService<IPatientService>();
            var summary = patientService.GetPatientClinicalSummary(_currentPatientId);
            if (summary is null || string.IsNullOrWhiteSpace(summary.AttendingDoctorId))
            {
                IsRxError = true;
                RxStatusMessage = "Validation error: Attending physician record is missing for the selected patient.";
                return;
            }

            var (prescription, directive) = patientService.SignPrescriptionAndQueueDirective(new PrescriptionOrderRequest(
                PatientId: _currentPatientId,
                DialyzerMembraneModel: DialyzerModel.Trim(),
                SurfaceAreaM2: surfaceArea,
                TargetDurationMinutes: durationMin,
                PrescribedBloodFlowMlMin: qb,
                PrescribedDialysateFlowMlMin: qd,
                DialysatePotassiumMeqL: potassium,
                DialysateCalciumMeqL: calcium,
                DialysateSodiumMeqL: sodium,
                DialysateBicarbonateMeqL: bicarbonate,
                AnticoagulantRxNormCui: anticoagulantCui,
                AnticoagulantBolusUnits: bolusUnits,
                AnticoagulantHourlyUnits: hourlyUnits,
                PrescribedByDoctorId: summary.AttendingDoctorId,
                SessionId: null,
                ClinicalRationale: string.IsNullOrWhiteSpace(PrescriptionRationaleText) ? null : PrescriptionRationaleText.Trim()));

            // Clear all Rx inputs back to empty after sign-off
            DialyzerModel = string.Empty;
            SurfaceAreaM2Text = string.Empty;
            DurationMinText = string.Empty;
            QbText = string.Empty;
            QdText = string.Empty;
            PotassiumMeqLText = string.Empty;
            CalciumMeqLText = string.Empty;
            SodiumMeqLText = string.Empty;
            BicarbonateMeqLText = string.Empty;
            HeparinBolusText = string.Empty;
            HeparinHourlyText = string.Empty;
            PrescriptionRationaleText = string.Empty;
            AnticoagulantRxNormCui = string.Empty;
            SelectedAnticoagulantCatalog = null;

            string updatedPatientId = _currentPatientId;
            LoadForPatient(updatedPatientId);
            _onPatientRecordUpdated?.Invoke(updatedPatientId);

            IsRxError = false;
            RxStatusMessage = SanitizeIsmpText(
                $"Prescription signed ({FormatCleanRouteDoseText(directive.IsmpFormattedDoseText)}) and sent to bedside nursing ({ClinicalDisplayFormatter.FormatHandshakeStatus(directive.Status)}).");
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or ArgumentException)
        {
            IsRxError = true;
            RxStatusMessage = $"Unable to sign prescription: {ex.Message}";
        }
    }

    private void ExecuteExportFhirBundle()
    {
        if (_currentPatientId is null || _lastSessionConfigInput is null || _lastSessionConfigResult is null)
        {
            FhirBundleJsonText = "Select a patient first to generate an HL7 FHIR R4 Bundle.";
            IsFhirDrawerExpanded = true;
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var exporter = scope.ServiceProvider.GetRequiredService<IFhirR4SessionBundleExporter>();
        var bundle = exporter.ExportToFhirR4Bundle(
            _lastSessionConfigInput,
            _lastSessionConfigResult.RecommendedParameters,
            _lastSpKtV > 0.0 ? _lastSpKtV : 1.40);

        FhirBundleJsonText = exporter.SerializeBundleToJson(bundle);
        IsFhirDrawerExpanded = true;
    }

    private void ExecuteFillStandardDefaults()
    {
        if (_currentPatientId is null)
        {
            IsRxError = true;
            RxStatusMessage = "Select a patient before filling prescription defaults.";
            return;
        }

        var recommended = _lastSessionConfigResult?.RecommendedParameters;
        double durationMinutes = recommended is not null
            ? Math.Round(recommended.TargetDurationHours * 60.0)
            : 240.0;
        double qb = recommended is not null
            ? Math.Round(recommended.BloodFlowRateMlMin)
            : 350.0;
        double qd = recommended is not null
            ? Math.Round(recommended.DialysateFlowRateMlMin)
            : 500.0;
        double kMeq = recommended?.DialysatePotassiumMeqL ?? 2.0;
        double naMeq = recommended?.DialysateSodiumMeqL ?? 138.0;
        double hco3Meq = recommended?.DialysateBicarbonateMeqL ?? 32.0;

        DialyzerModel = "FX-80";
        SurfaceAreaM2Text = "1.8";
        DurationMinText = durationMinutes.ToString("0", CultureInfo.InvariantCulture);
        QbText = qb.ToString("0", CultureInfo.InvariantCulture);
        QdText = qd.ToString("0", CultureInfo.InvariantCulture);
        PotassiumMeqLText = kMeq.ToString("0.##", CultureInfo.InvariantCulture);
        CalciumMeqLText = "1.25";
        SodiumMeqLText = naMeq.ToString("0.##", CultureInfo.InvariantCulture);
        BicarbonateMeqLText = hco3Meq.ToString("0.##", CultureInfo.InvariantCulture);
        AnticoagulantRxNormCui = "5224";
        SelectedAnticoagulantCatalog ??= RxNormCatalogItems
            .FirstOrDefault(m => string.Equals(m.RxNormCui, "5224", StringComparison.Ordinal))
            ?? RxNormCatalogItems.FirstOrDefault();
        HeparinBolusText = "2000";
        HeparinHourlyText = "500";
        PrescriptionRationaleText = "Standard adult hemodialysis prescription";

        IsRxError = false;
        RxStatusMessage = "Standard defaults filled — review every parameter before signing.";
    }

    private static (ActiveSessionBoardView? ActiveBoard, SessionHistoryProjection? LatestCompleted, string? SessionId)
        ResolveLatestSessionContext(
            ISessionService sessionService,
            ISessionRepository sessionRepository,
            string patientId)
    {
        var candidateDates = new[]
        {
            "2026-11-10",
            DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };

        ActiveSessionBoardView? active = null;
        foreach (string date in candidateDates.Distinct(StringComparer.Ordinal))
        {
            for (int shift = 1; shift <= 4; shift++)
            {
                var board = sessionService.GetActiveShiftBoard(date, shift);
                active = board.FirstOrDefault(b => string.Equals(b.PatientId, patientId, StringComparison.Ordinal));
                if (active is not null)
                {
                    break;
                }
            }

            if (active is not null)
            {
                break;
            }
        }

        var completed = sessionRepository.GetRecentCompletedSessionsForPatient(patientId, 1).FirstOrDefault();
        string? sessionId = active?.SessionId ?? completed?.SessionId;
        return (active, completed, sessionId);
    }

    private static string FormatTitrationAction(TitrationAction action)
    {
        return action switch
        {
            TitrationAction.HoldDose => "Hold Dose",
            TitrationAction.ReduceDose25 => "Reduce Dose 25%",
            TitrationAction.MaintainDose => "Maintain Dose",
            TitrationAction.IncreaseDose25 => "Increase Dose 25%",
            TitrationAction.WithholdIvIron => "Withhold IV Iron",
            TitrationAction.AdministerIvIron => "Administer IV Iron",
            _ => action.ToString()
        };
    }

    private static string HumanizeSessionAlert(string rawAlert)
    {
        if (string.IsNullOrWhiteSpace(rawAlert))
        {
            return string.Empty;
        }

        string humanized = rawAlert
            .Replace("COOL_DIALYSATE_INDICATED:", "Cool Dialysate Indicated:", StringComparison.Ordinal)
            .Replace("PARADOXICAL_WEIGHT_DEFICIT:", "Pre-Dialysis Weight Below Dry Weight:", StringComparison.Ordinal)
            .Replace("UFR_CAP_EXCEEDED:", "Ultrafiltration Rate Cap Exceeded:", StringComparison.Ordinal);
        return SanitizeIsmpText(humanized);
    }

    private static string BuildIsmpSampleTallManBanner()
    {
        string h1 = IsmpMedicationFormatter.FormatPrescriptionLine("hydroxyzine", 25.0, "mg", "PO");
        string h2 = IsmpMedicationFormatter.FormatPrescriptionLine("hydralazine", 10.0, "mg", "PO");
        string h3 = IsmpMedicationFormatter.FormatPrescriptionLine("hydromorphone", 0.5, "mg", "IV");
        string vol = IsmpMedicationFormatter.FormatNumericDose(1.0, "L");
        string units = IsmpMedicationFormatter.FormatNumericDose(5000.0, "u");
        return $"ISMP LASA / High-Alert Reference: {h1} | {h2} | {h3} | Prime {vol} | Ref {units}";
    }

    private static bool TryParseBounded(string raw, double minInclusive, double maxInclusive, out double value)
    {
        if (!double.TryParse(raw?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
            !double.IsFinite(value))
        {
            return false;
        }

        return value >= minInclusive && value <= maxInclusive;
    }
}
