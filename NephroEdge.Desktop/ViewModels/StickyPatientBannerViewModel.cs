using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using NephroEdge.Desktop.Infrastructure;
using Persistence_Unit.Entities;
using Services_Unit.Core.Formatting;
using Services_Unit.Core.Interfaces;
using Services_Unit.RiskPrediction.Contracts;

namespace NephroEdge.Desktop.ViewModels;

/// <summary>
/// IPSG.1 Sticky 2-Identifier Patient Banner ViewModel pinned across all 4 desktop workspaces.
/// Computes all clinical metadata directly from the active PatientSummaryView to prevent stale context on patient switch.
/// </summary>
public sealed class StickyPatientBannerViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Action<string?>? _onSelectedPatientChanged;
    private readonly Dictionary<string, (string BedLabel, AlarmPriorityTier AlarmPriority)> _shiftContextByPatientId =
        new(StringComparer.Ordinal);

    private PatientSummaryView? _selectedPatient;
    private PatientSwitcherOption? _selectedSwitcherItem;
    private bool _suppressSelectionCallback;
    private bool _syncingSelection;

    public ObservableCollection<PatientSummaryView> AllPatients { get; } = new();
    public ObservableCollection<PatientSwitcherOption> SwitcherItems { get; } = new();

    public PatientSummaryView? SelectedPatient
    {
        get => _selectedPatient;
        set
        {
            if (SetProperty(ref _selectedPatient, value))
            {
                SyncSelectedSwitcherFromPatient(value);
                RaiseBannerFieldChanges();
                if (!_suppressSelectionCallback)
                {
                    _onSelectedPatientChanged?.Invoke(value?.PatientId);
                }
            }
        }
    }

    public PatientSwitcherOption? SelectedSwitcherItem
    {
        get => _selectedSwitcherItem;
        set
        {
            if (SetProperty(ref _selectedSwitcherItem, value) && !_syncingSelection)
            {
                if (value is not null &&
                    !string.Equals(_selectedPatient?.PatientId, value.PatientId, StringComparison.Ordinal))
                {
                    SelectedPatient = value.Patient;
                }
            }
        }
    }

    public string? SelectedPatientId => _selectedPatient?.PatientId;
    public bool HasSelectedPatient => _selectedPatient is not null;

    public string FullNameText =>
        _selectedPatient is null || string.IsNullOrWhiteSpace(_selectedPatient.FullName)
            ? "No Patient Selected"
            : _selectedPatient.FullName;

    public string InitialsText
    {
        get
        {
            if (_selectedPatient is null || string.IsNullOrWhiteSpace(_selectedPatient.FullName))
            {
                return "--";
            }

            string[] parts = _selectedPatient.FullName
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                return "--";
            }

            if (parts.Length == 1)
            {
                return parts[0].Length >= 2
                    ? parts[0][..2].ToUpperInvariant()
                    : parts[0].ToUpperInvariant();
            }

            char first = parts[0][0];
            char last = parts[^1][0];
            return string.Concat(char.ToUpperInvariant(first), char.ToUpperInvariant(last));
        }
    }

    public string MrnText =>
        _selectedPatient is null
            ? "MRN: --"
            : $"MRN: {_selectedPatient.MedicalRecordNumber}";

    public string MrnPlainText =>
        _selectedPatient is null
            ? "--"
            : _selectedPatient.MedicalRecordNumber;

    public string PatientIdText =>
        _selectedPatient is null
            ? "ID: --"
            : $"ID: {_selectedPatient.PatientId}";

    public string PatientIdPlainText =>
        _selectedPatient is null
            ? "--"
            : _selectedPatient.PatientId;

    public string DualIdentifierText =>
        _selectedPatient is null
            ? "Select a patient from Shift Grid or Patient Switcher"
            : $"MRN {_selectedPatient.MedicalRecordNumber}  ·  ID {_selectedPatient.PatientId}";

    public string SexAgeText =>
        _selectedPatient is null
            ? "--"
            : $"{_selectedPatient.BiologicalSex} · {_selectedPatient.CalculatedAgeYears} yrs";

    public string BloodTypeText =>
        _selectedPatient is null || string.IsNullOrWhiteSpace(_selectedPatient.BloodType)
            ? "--"
            : _selectedPatient.BloodType;

    public string CompactDemographicsText
    {
        get
        {
            if (_selectedPatient is null)
            {
                return "--";
            }

            string sexShort = _selectedPatient.BiologicalSex?.Trim() switch
            {
                var s when string.Equals(s, "Female", StringComparison.OrdinalIgnoreCase) => "F",
                var s when string.Equals(s, "Male", StringComparison.OrdinalIgnoreCase) => "M",
                var s when !string.IsNullOrEmpty(s) => s[..1].ToUpperInvariant(),
                _ => string.Empty
            };

            string ageSex = $"{_selectedPatient.CalculatedAgeYears}{sexShort}";
            return string.IsNullOrWhiteSpace(_selectedPatient.BloodType)
                ? ageSex
                : $"{ageSex} · {_selectedPatient.BloodType}";
        }
    }

    public string CurrentBedText =>
        _selectedPatient is not null &&
        _shiftContextByPatientId.TryGetValue(_selectedPatient.PatientId, out var ctx) &&
        !string.IsNullOrWhiteSpace(ctx.BedLabel)
            ? ctx.BedLabel
            : "--";

    public bool IsIsolationRequired => _selectedPatient?.IsolationRequired == 1;
    public bool IsStandardPrecautions => _selectedPatient is not null && _selectedPatient.IsolationRequired != 1;

    public string IsolationBadgeText =>
        _selectedPatient is null
            ? "NO PATIENT"
            : (IsIsolationRequired ? "◆ HBV+ ISOLATION" : "● STANDARD PRECAUTIONS");

    public string CompactIsolationText =>
        _selectedPatient is null
            ? "No Patient"
            : (IsIsolationRequired ? "HBV+" : "Standard");

    public string VascularAccessText
    {
        get
        {
            if (_selectedPatient is null)
            {
                return "--";
            }

            string rawType = _selectedPatient.CurrentAccessType?.Trim() ?? string.Empty;
            string accessSite = _selectedPatient.CurrentAccessSite?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(rawType) && string.IsNullOrEmpty(accessSite))
            {
                return "Access Not Recorded";
            }

            if (string.IsNullOrEmpty(accessSite))
            {
                return ClinicalDisplayFormatter.FormatVascularAccess(rawType);
            }

            if (string.IsNullOrEmpty(rawType))
            {
                return ClinicalDisplayFormatter.FormatAccessSite(accessSite);
            }

            return $"{ClinicalDisplayFormatter.FormatVascularAccess(rawType)} · {ClinicalDisplayFormatter.FormatAccessSite(accessSite)}";
        }
    }

    public string DryWeightText =>
        _selectedPatient?.CurrentDryWeightKg is double weightKg && double.IsFinite(weightKg) && weightKg > 0.0
            ? IsmpMedicationFormatter.FormatNumericDose(Math.Round(weightKg, 2), "kg")
            : "-- kg";

    public string EtiologyAndAttendingText
    {
        get
        {
            if (_selectedPatient is null)
            {
                return "--";
            }

            string code = string.IsNullOrWhiteSpace(_selectedPatient.PrimaryEtiologyCode)
                ? "ESRD"
                : _selectedPatient.PrimaryEtiologyCode;

            string doctor = string.IsNullOrWhiteSpace(_selectedPatient.AttendingDoctorLastName)
                ? "Unassigned"
                : $"Dr. {_selectedPatient.AttendingDoctorLastName}";

            return $"{code} · {doctor}";
        }
    }

    public StickyPatientBannerViewModel(
        IServiceScopeFactory scopeFactory,
        Action<string?>? onSelectedPatientChanged = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _onSelectedPatientChanged = onSelectedPatientChanged;
    }

    public void UpdateShiftContext(IEnumerable<ShiftBedTileItem>? activeBeds)
    {
        _shiftContextByPatientId.Clear();
        if (activeBeds is not null)
        {
            foreach (var bed in activeBeds)
            {
                if (string.IsNullOrWhiteSpace(bed.PatientId))
                {
                    continue;
                }

                if (_shiftContextByPatientId.TryGetValue(bed.PatientId, out var existing))
                {
                    AlarmPriorityTier bestTier = bed.EffectiveAlarmPriority > existing.AlarmPriority
                        ? bed.EffectiveAlarmPriority
                        : existing.AlarmPriority;
                    string bedLabel = string.IsNullOrWhiteSpace(existing.BedLabel)
                        ? bed.ChairOrBedNumber
                        : existing.BedLabel;
                    _shiftContextByPatientId[bed.PatientId] = (bedLabel, bestTier);
                }
                else
                {
                    _shiftContextByPatientId[bed.PatientId] = (bed.ChairOrBedNumber, bed.EffectiveAlarmPriority);
                }
            }
        }

        RebuildSwitcherItems();
        OnPropertyChanged(nameof(CurrentBedText));
    }

    public void LoadPatients(string? preferredPatientId = null)
    {
        string? targetId = !string.IsNullOrWhiteSpace(preferredPatientId)
            ? preferredPatientId
            : _selectedPatient?.PatientId;

        using var scope = _scopeFactory.CreateScope();
        var patientService = scope.ServiceProvider.GetRequiredService<IPatientService>();
        var summaries = patientService.GetAllPatientSummaries();

        _suppressSelectionCallback = true;
        try
        {
            AllPatients.Clear();
            foreach (var summary in summaries)
            {
                AllPatients.Add(summary);
            }

            PatientSummaryView? match = !string.IsNullOrWhiteSpace(targetId)
                ? AllPatients.FirstOrDefault(p => string.Equals(p.PatientId, targetId, StringComparison.Ordinal))
                : null;

            match ??= AllPatients.FirstOrDefault();

            _selectedPatient = match;
            RebuildSwitcherItems();
            OnPropertyChanged(nameof(SelectedPatient));
            RaiseBannerFieldChanges();
        }
        finally
        {
            _suppressSelectionCallback = false;
        }
    }

    public bool SelectPatientById(string? patientId, bool notifyListeners = false)
    {
        string? previousPatientId = _selectedPatient?.PatientId;

        if (string.IsNullOrWhiteSpace(patientId))
        {
            SetSelectedPatientInternal(null, notifyListeners, previousPatientId);
            return false;
        }

        PatientSummaryView? match = AllPatients.FirstOrDefault(p =>
            string.Equals(p.PatientId, patientId, StringComparison.Ordinal));

        if (match is null)
        {
            LoadPatients(patientId);
            match = AllPatients.FirstOrDefault(p =>
                string.Equals(p.PatientId, patientId, StringComparison.Ordinal));

            if (match is null)
            {
                using var scope = _scopeFactory.CreateScope();
                var patientService = scope.ServiceProvider.GetRequiredService<IPatientService>();
                PatientSummaryView? single = patientService.GetPatientClinicalSummary(patientId);
                if (single is not null)
                {
                    _suppressSelectionCallback = true;
                    try
                    {
                        AllPatients.Add(single);
                        RebuildSwitcherItems();
                    }
                    finally
                    {
                        _suppressSelectionCallback = false;
                    }

                    match = single;
                }
            }
        }

        if (match is null)
        {
            return false;
        }

        SetSelectedPatientInternal(match, notifyListeners, previousPatientId);
        return true;
    }

    private void SetSelectedPatientInternal(
        PatientSummaryView? match,
        bool notifyListeners,
        string? previousPatientId)
    {
        bool previousSuppress = _suppressSelectionCallback;
        _suppressSelectionCallback = !notifyListeners;
        try
        {
            bool changed = SetProperty(ref _selectedPatient, match, nameof(SelectedPatient));
            SyncSelectedSwitcherFromPatient(match);
            RaiseBannerFieldChanges();
            if (!_suppressSelectionCallback &&
                (changed || !string.Equals(previousPatientId, match?.PatientId, StringComparison.Ordinal)))
            {
                _onSelectedPatientChanged?.Invoke(match?.PatientId);
            }
        }
        finally
        {
            _suppressSelectionCallback = previousSuppress;
        }
    }

    private void RebuildSwitcherItems()
    {
        _syncingSelection = true;
        try
        {
            SwitcherItems.Clear();
            foreach (var patient in AllPatients)
            {
                _shiftContextByPatientId.TryGetValue(patient.PatientId, out var ctx);
                SwitcherItems.Add(new PatientSwitcherOption(
                    patient,
                    ctx.BedLabel,
                    ctx.AlarmPriority));
            }

            SyncSelectedSwitcherFromPatient(_selectedPatient);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void SyncSelectedSwitcherFromPatient(PatientSummaryView? patient)
    {
        bool prev = _syncingSelection;
        _syncingSelection = true;
        try
        {
            PatientSwitcherOption? option = patient is null
                ? null
                : SwitcherItems.FirstOrDefault(i =>
                    string.Equals(i.PatientId, patient.PatientId, StringComparison.Ordinal));

            if (!ReferenceEquals(_selectedSwitcherItem, option))
            {
                _selectedSwitcherItem = option;
                OnPropertyChanged(nameof(SelectedSwitcherItem));
            }
        }
        finally
        {
            _syncingSelection = prev;
        }
    }

    private void RaiseBannerFieldChanges()
    {
        OnPropertyChanged(nameof(SelectedPatientId));
        OnPropertyChanged(nameof(HasSelectedPatient));
        OnPropertyChanged(nameof(FullNameText));
        OnPropertyChanged(nameof(InitialsText));
        OnPropertyChanged(nameof(MrnText));
        OnPropertyChanged(nameof(MrnPlainText));
        OnPropertyChanged(nameof(PatientIdText));
        OnPropertyChanged(nameof(PatientIdPlainText));
        OnPropertyChanged(nameof(DualIdentifierText));
        OnPropertyChanged(nameof(SexAgeText));
        OnPropertyChanged(nameof(BloodTypeText));
        OnPropertyChanged(nameof(CompactDemographicsText));
        OnPropertyChanged(nameof(CurrentBedText));
        OnPropertyChanged(nameof(IsIsolationRequired));
        OnPropertyChanged(nameof(IsStandardPrecautions));
        OnPropertyChanged(nameof(IsolationBadgeText));
        OnPropertyChanged(nameof(CompactIsolationText));
        OnPropertyChanged(nameof(VascularAccessText));
        OnPropertyChanged(nameof(DryWeightText));
        OnPropertyChanged(nameof(EtiologyAndAttendingText));
    }
}
