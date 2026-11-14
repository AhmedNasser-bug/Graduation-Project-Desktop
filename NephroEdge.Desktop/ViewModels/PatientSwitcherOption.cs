using System;
using Avalonia.Media;
using Persistence_Unit.Entities;
using Services_Unit.RiskPrediction.Contracts;

namespace NephroEdge.Desktop.ViewModels;

/// <summary>
/// Enriched presentation option for the IPSG.1 Patient Switcher ComboBox, surfacing
/// full patient name, MRN, active shift bed assignment, IEC 60601-1-8 alarm cue,
/// and HBV+ isolation flag without altering the underlying <see cref="PatientSummaryView"/>.
/// </summary>
public sealed class PatientSwitcherOption
{
    private static readonly IBrush HighRedBrush = new SolidColorBrush(Color.Parse("#DC2626")).ToImmutable();
    private static readonly IBrush MediumAmberBrush = new SolidColorBrush(Color.Parse("#D97706")).ToImmutable();
    private static readonly IBrush NominalTealBrush = new SolidColorBrush(Color.Parse("#0F766E")).ToImmutable();

    public PatientSwitcherOption(
        PatientSummaryView patient,
        string? bedLabel = null,
        AlarmPriorityTier alarmPriority = AlarmPriorityTier.None)
    {
        Patient = patient ?? throw new ArgumentNullException(nameof(patient));
        BedLabel = string.IsNullOrWhiteSpace(bedLabel) ? null : bedLabel.Trim();
        AlarmPriority = alarmPriority;

        AlarmGlyph = alarmPriority switch
        {
            AlarmPriorityTier.HighRed => "▲",
            AlarmPriorityTier.MediumYellow => "◆",
            _ => "●"
        };

        AlarmGlyphBrush = alarmPriority switch
        {
            AlarmPriorityTier.HighRed => HighRedBrush,
            AlarmPriorityTier.MediumYellow => MediumAmberBrush,
            _ => NominalTealBrush
        };
    }

    public PatientSummaryView Patient { get; }
    public string PatientId => Patient.PatientId;
    public string FullName => Patient.FullName;
    public string MedicalRecordNumber => Patient.MedicalRecordNumber;
    public string? BedLabel { get; }
    public bool HasBedLabel => !string.IsNullOrWhiteSpace(BedLabel);
    public AlarmPriorityTier AlarmPriority { get; }
    public string AlarmGlyph { get; }
    public IBrush AlarmGlyphBrush { get; }
    public bool IsIsolationRequired => Patient.IsolationRequired == 1;
    public string IsolationTag => IsIsolationRequired ? "HBV+" : string.Empty;

    public string SecondaryLineText => HasBedLabel
        ? $"{BedLabel} · {MedicalRecordNumber}"
        : MedicalRecordNumber;
}
