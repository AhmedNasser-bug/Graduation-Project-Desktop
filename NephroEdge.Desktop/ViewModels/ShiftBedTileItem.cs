using System;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using NephroEdge.Desktop.Infrastructure;
using Persistence_Unit.Entities;
using Services_Unit.RiskPrediction.Contracts;

namespace NephroEdge.Desktop.ViewModels;

/// <summary>
/// Presentation item for a single dialysis bed tile inside Workspace 1 (Active Shift Risk Grid).
/// Enforces IEC 60601-1-8 multi-modal alarm encoding (color + geometric shape + text badge + flash cadence)
/// and reserves Red exclusively for high-priority clinical alarms.
/// </summary>
public sealed class ShiftBedTileItem : ObservableObject
{
    private static readonly IBrush HighRedStrongBrush = new SolidColorBrush(Color.Parse("#D92D20")).ToImmutable();
    private static readonly IBrush HighRedDimBorderBrush = new SolidColorBrush(Color.Parse("#FCA5A5")).ToImmutable();
    private static readonly IBrush HighRedSoftBgBrush = new SolidColorBrush(Color.Parse("#FEE2E2")).ToImmutable();
    private static readonly IBrush HighRedInkBrush = new SolidColorBrush(Color.Parse("#991B1B")).ToImmutable();
    private static readonly IBrush HighRedCardTintBrush = new SolidColorBrush(Color.Parse("#FEF2F2")).ToImmutable();

    private static readonly IBrush MediumAmberStrongBrush = new SolidColorBrush(Color.Parse("#D97706")).ToImmutable();
    private static readonly IBrush MediumAmberDimBorderBrush = new SolidColorBrush(Color.Parse("#FCD34D")).ToImmutable();
    private static readonly IBrush MediumAmberSoftBgBrush = new SolidColorBrush(Color.Parse("#FEF3C7")).ToImmutable();
    private static readonly IBrush MediumAmberInkBrush = new SolidColorBrush(Color.Parse("#92400E")).ToImmutable();
    private static readonly IBrush MediumAmberCardTintBrush = new SolidColorBrush(Color.Parse("#FFFBEB")).ToImmutable();

    private static readonly IBrush NominalTealBrush = new SolidColorBrush(Color.Parse("#0F766E")).ToImmutable();
    private static readonly IBrush NominalTealSoftBgBrush = new SolidColorBrush(Color.Parse("#F0FDFA")).ToImmutable();
    private static readonly IBrush NominalTealBorderBrush = new SolidColorBrush(Color.Parse("#99F6E4")).ToImmutable();
    private static readonly IBrush NominalTealInkBrush = new SolidColorBrush(Color.Parse("#115E59")).ToImmutable();

    private static readonly IBrush TechCyanBrush = new SolidColorBrush(Color.Parse("#0E7490")).ToImmutable();
    private static readonly IBrush TechCyanSoftBgBrush = new SolidColorBrush(Color.Parse("#ECFEFF")).ToImmutable();
    private static readonly IBrush TechCyanInkBrush = new SolidColorBrush(Color.Parse("#155E75")).ToImmutable();

    private static readonly IBrush SoftBorderBrush = new SolidColorBrush(Color.Parse("#E2E8F0")).ToImmutable();
    private static readonly IBrush InkBrush = new SolidColorBrush(Color.Parse("#0F172A")).ToImmutable();
    private static readonly IBrush MutedInkBrush = new SolidColorBrush(Color.Parse("#475569")).ToImmutable();
    private static readonly IBrush SelectedHighlightBrush = new SolidColorBrush(Color.Parse("#0F766E")).ToImmutable();
    private static readonly IBrush WhiteBrush = Brushes.White;
    private static readonly BoxShadows SelectedCardShadows = BoxShadows.Parse("0 0 0 3 #380F766E, 0 4 12 0 #140F172A");
    private static readonly BoxShadows DefaultCardShadows = BoxShadows.Parse("0 2 8 0 #0D0F172A");

    private bool _isFlashEmphasized;
    private bool _isSelected;
    private IBrush _alarmBorderBrush;
    private Thickness _alarmBorderThickness;
    private IBrush _alarmBadgeBackgroundBrush;
    private IBrush _alarmBadgeBorderBrush;
    private IBrush _alarmBadgeForegroundBrush;
    private IBrush _alarmCardBackgroundBrush;

    public ShiftBedTileItem(
        ActiveSessionBoardView boardRow,
        IntradialyticRiskAssessmentResult? riskAssessment,
        AlarmPriorityTier effectiveAlarmPriority,
        long latestSystolicBp,
        long latestDiastolicBp,
        long latestHeartRateBpm,
        double deltaSbpMmHg,
        double latestUfRateMlHr,
        double remainingUfMl,
        bool hasTelemetryTicks,
        string triggerRuleSummary,
        string pendingAckBadgeText,
        int pendingHandshakeCount,
        string? primaryHandshakeId = null,
        string? conciseStatusText = null)
    {
        ArgumentNullException.ThrowIfNull(boardRow);

        SessionId = boardRow.SessionId;
        PatientId = boardRow.PatientId;
        ChairOrBedNumber = boardRow.ChairOrBedNumber;
        PatientFullName = boardRow.PatientFullName;
        MedicalRecordNumber = boardRow.MedicalRecordNumber;
        SessionStatus = boardRow.SessionStatus;
        SessionStatusDisplay = ClinicalDisplayFormatter.FormatSessionStatus(boardRow.SessionStatus);
        ShiftNumber = boardRow.ShiftNumber;
        SessionDate = boardRow.SessionDate;

        IsIsolationRequired = boardRow.IsolationRequired == 1;
        IsolationBadgeText = IsIsolationRequired ? "HBV+" : string.Empty;

        PreSystolicBp = boardRow.PreSystolicBp;
        PreDiastolicBp = boardRow.PreDiastolicBp;
        LatestSystolicBp = latestSystolicBp;
        LatestDiastolicBp = latestDiastolicBp;
        LatestHeartRateBpm = latestHeartRateBpm;
        DeltaSbpMmHg = Math.Round(deltaSbpMmHg, 1);
        LatestUfRateMlHr = Math.Round(latestUfRateMlHr, 0);
        TargetUltrafiltrationMl = boardRow.TargetUltrafiltrationMl;
        RemainingUfMl = Math.Round(remainingUfMl, 0);
        if (TargetUltrafiltrationMl > 0.0)
        {
            double removedMl = Math.Max(0.0, TargetUltrafiltrationMl - RemainingUfMl);
            UfProgressPercent = Math.Clamp(Math.Round((removedMl / TargetUltrafiltrationMl) * 100.0, 1), 0.0, 100.0);
        }
        else
        {
            UfProgressPercent = 0.0;
        }
        TargetDryWeightKg = boardRow.TargetDryWeightKg;
        PreDialysisWeightKg = boardRow.PreDialysisWeightKg;
        FluidGainOverDryWeightKg = boardRow.FluidGainOverDryWeightKg;
        ActiveIncidentCount = boardRow.ActiveUnresolvedIncidentCount;
        HasTelemetryTicks = hasTelemetryTicks;

        RiskAssessment = riskAssessment;
        EffectiveAlarmPriority = effectiveAlarmPriority;
        RiskBand = riskAssessment?.RiskBand ?? (effectiveAlarmPriority == AlarmPriorityTier.HighRed
            ? RiskBand.Critical
            : effectiveAlarmPriority == AlarmPriorityTier.MediumYellow
                ? RiskBand.High
                : RiskBand.Low);
        IdhProbabilityScore = riskAssessment?.IdhProbabilityScore ?? 0.0;

        TriggerRuleSummary = string.IsNullOrWhiteSpace(triggerRuleSummary)
            ? "Within KDOQI / Flythe nominal limits"
            : triggerRuleSummary;

        PrimaryHandshakeId = primaryHandshakeId;
        PendingAckBadgeText = pendingAckBadgeText;
        PendingHandshakeCount = pendingHandshakeCount;
        HasConflictHandshake = string.Equals(pendingAckBadgeText, "Order Conflict", StringComparison.Ordinal)
                            || string.Equals(pendingAckBadgeText, "CONFLICT", StringComparison.Ordinal);
        HasPendingHandshake = string.Equals(pendingAckBadgeText, "Pending Nurse Ack", StringComparison.Ordinal)
                           || string.Equals(pendingAckBadgeText, "PENDING NURSE ACK", StringComparison.Ordinal);

        if (HasConflictHandshake)
        {
            PendingAckGlyph = "◆";
            PendingAckBackgroundBrush = MediumAmberSoftBgBrush;
            PendingAckBorderBrush = MediumAmberStrongBrush;
            PendingAckForegroundBrush = MediumAmberInkBrush;
        }
        else if (HasPendingHandshake)
        {
            PendingAckGlyph = "◈";
            PendingAckBackgroundBrush = TechCyanSoftBgBrush;
            PendingAckBorderBrush = TechCyanBrush;
            PendingAckForegroundBrush = TechCyanInkBrush;
        }
        else
        {
            PendingAckGlyph = "✓";
            PendingAckBackgroundBrush = NominalTealSoftBgBrush;
            PendingAckBorderBrush = NominalTealBrush;
            PendingAckForegroundBrush = NominalTealInkBrush;
        }

        HasActionableHandshake = HasConflictHandshake || HasPendingHandshake;
        IsOrdersConfirmed = !HasActionableHandshake;
        PendingAckActionText = HasConflictHandshake
            ? "Order Conflict · Review →"
            : HasPendingHandshake
                ? "Pending Nurse Ack · Review →"
                : "Orders Confirmed";

        LatestBpDisplay = hasTelemetryTicks
            ? $"{LatestSystolicBp}/{LatestDiastolicBp} mmHg"
            : $"{LatestSystolicBp}/{LatestDiastolicBp} mmHg (Pre)";
        HeroBpDisplay = $"{LatestSystolicBp}/{LatestDiastolicBp}";
        PreBpDisplay = $"Pre {PreSystolicBp}/{PreDiastolicBp}";
        LatestAndPreBpDisplay = $"{LatestSystolicBp}/{LatestDiastolicBp} · Pre {PreSystolicBp}/{PreDiastolicBp}";

        DeltaSbpDisplay = DeltaSbpMmHg >= 0.0
            ? $"-{DeltaSbpMmHg.ToString("0.#", CultureInfo.InvariantCulture)} mmHg"
            : $"+{Math.Abs(DeltaSbpMmHg).ToString("0.#", CultureInfo.InvariantCulture)} mmHg";

        UfRateDisplay = ClinicalDisplayFormatter.FormatRateMlHr(LatestUfRateMlHr);
        RemainingUfDisplay = $"{ClinicalDisplayFormatter.FormatVolumeMlOrL(RemainingUfMl)} left";
        UfAndRemainingDisplay = $"{UfRateDisplay} · {RemainingUfDisplay}";

        FluidSummaryDisplay = string.Create(
            CultureInfo.InvariantCulture,
            $"Gain {FluidGainOverDryWeightKg:+0.##;-0.##;0} kg · Dry {TargetDryWeightKg:0.##} kg");

        int riskPercent = (int)Math.Round(Math.Clamp(IdhProbabilityScore, 0.0, 1.0) * 100.0);
        RiskScoreDisplay = $"IDH {riskPercent}% · {LatestHeartRateBpm} bpm";
        HeartRateDisplay = $"{LatestHeartRateBpm} bpm";
        IncidentSummaryDisplay = ActiveIncidentCount > 0
            ? $"{ActiveIncidentCount} open incident(s)"
            : "0 open incidents";

        ConciseStatusText = string.IsNullOrWhiteSpace(conciseStatusText)
            ? TriggerRuleSummary
            : conciseStatusText;

        AlarmGlyph = EffectiveAlarmPriority switch
        {
            AlarmPriorityTier.HighRed => "▲",
            AlarmPriorityTier.MediumYellow => "◆",
            _ => "●"
        };

        AlarmBadgeText = EffectiveAlarmPriority switch
        {
            AlarmPriorityTier.HighRed when LatestSystolicBp < 90 => "▲ Low BP",
            AlarmPriorityTier.HighRed when DeltaSbpMmHg >= 20.0 => "▲ BP Drop",
            AlarmPriorityTier.HighRed => "▲ High Alert",
            AlarmPriorityTier.MediumYellow => "◆ Warning",
            _ => "● Stable"
        };

        PrimaryActionText = IsHighAlarm || IsMediumWarning
            ? "Review Alert →"
            : "Open Chart →";

        _alarmBorderBrush = SoftBorderBrush;
        _alarmBorderThickness = new Thickness(1);
        _alarmBadgeBackgroundBrush = NominalTealSoftBgBrush;
        _alarmBadgeBorderBrush = NominalTealBorderBrush;
        _alarmBadgeForegroundBrush = NominalTealInkBrush;
        _alarmCardBackgroundBrush = WhiteBrush;

        ApplyFlashState(highFlashOn: true, mediumFlashOn: true);
    }

    public string SessionId { get; }
    public string PatientId { get; }
    public string ChairOrBedNumber { get; }
    public string PatientFullName { get; }
    public string MedicalRecordNumber { get; }
    public string SessionStatus { get; }
    public string SessionStatusDisplay { get; }
    public long ShiftNumber { get; }
    public string SessionDate { get; }

    public bool IsIsolationRequired { get; }
    public string IsolationBadgeText { get; }

    public long PreSystolicBp { get; }
    public long PreDiastolicBp { get; }
    public long LatestSystolicBp { get; }
    public long LatestDiastolicBp { get; }
    public long LatestHeartRateBpm { get; }
    public double DeltaSbpMmHg { get; }
    public double LatestUfRateMlHr { get; }
    public double TargetUltrafiltrationMl { get; }
    public double RemainingUfMl { get; }
    public double UfProgressPercent { get; }
    public double TargetDryWeightKg { get; }
    public double PreDialysisWeightKg { get; }
    public double FluidGainOverDryWeightKg { get; }
    public long ActiveIncidentCount { get; }
    public bool HasTelemetryTicks { get; }

    public IntradialyticRiskAssessmentResult? RiskAssessment { get; }
    public AlarmPriorityTier EffectiveAlarmPriority { get; }
    public RiskBand RiskBand { get; }
    public double IdhProbabilityScore { get; }
    public string TriggerRuleSummary { get; }
    public string ConciseStatusText { get; }

    public bool IsHighAlarm => EffectiveAlarmPriority == AlarmPriorityTier.HighRed;
    public bool IsMediumWarning => EffectiveAlarmPriority == AlarmPriorityTier.MediumYellow;
    public bool IsNominal => EffectiveAlarmPriority == AlarmPriorityTier.None;

    public string AlarmGlyph { get; }
    public string AlarmBadgeText { get; }
    public string PrimaryActionText { get; }

    public string? PrimaryHandshakeId { get; }
    public string PendingAckGlyph { get; }
    public string PendingAckBadgeText { get; }
    public int PendingHandshakeCount { get; }
    public bool HasPendingHandshake { get; }
    public bool HasConflictHandshake { get; }
    public bool HasActionableHandshake { get; }
    public bool IsOrdersConfirmed { get; }
    public string PendingAckActionText { get; }
    public IBrush PendingAckBackgroundBrush { get; }
    public IBrush PendingAckBorderBrush { get; }
    public IBrush PendingAckForegroundBrush { get; }

    public string LatestBpDisplay { get; }
    public string HeroBpDisplay { get; }
    public string PreBpDisplay { get; }
    public string LatestAndPreBpDisplay { get; }
    public string DeltaSbpDisplay { get; }
    public string UfRateDisplay { get; }
    public string RemainingUfDisplay { get; }
    public string UfAndRemainingDisplay { get; }
    public string FluidSummaryDisplay { get; }
    public string RiskScoreDisplay { get; }
    public string HeartRateDisplay { get; }
    public string IncidentSummaryDisplay { get; }

    public IBrush HeroBpForegroundBrush => IsHighAlarm ? HighRedInkBrush : InkBrush;

    public IBrush ConciseStatusForegroundBrush => EffectiveAlarmPriority switch
    {
        AlarmPriorityTier.HighRed => HighRedInkBrush,
        AlarmPriorityTier.MediumYellow => MediumAmberInkBrush,
        _ => MutedInkBrush
    };

    public FontWeight ConciseStatusFontWeight =>
        IsNominal ? FontWeight.Normal : FontWeight.SemiBold;

    public IBrush PrimaryActionBackgroundBrush => EffectiveAlarmPriority switch
    {
        AlarmPriorityTier.HighRed => HighRedStrongBrush,
        AlarmPriorityTier.MediumYellow => MediumAmberSoftBgBrush,
        _ => WhiteBrush
    };

    public IBrush PrimaryActionBorderBrush => EffectiveAlarmPriority switch
    {
        AlarmPriorityTier.HighRed => HighRedStrongBrush,
        AlarmPriorityTier.MediumYellow => MediumAmberStrongBrush,
        _ => SoftBorderBrush
    };

    public IBrush PrimaryActionForegroundBrush => EffectiveAlarmPriority switch
    {
        AlarmPriorityTier.HighRed => WhiteBrush,
        AlarmPriorityTier.MediumYellow => MediumAmberInkBrush,
        _ => InkBrush
    };

    public bool IsFlashEmphasized
    {
        get => _isFlashEmphasized;
        private set => SetProperty(ref _isFlashEmphasized, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(CardOuterHighlightBrush));
                OnPropertyChanged(nameof(CardBoxShadow));
                OnPropertyChanged(nameof(SelectButtonLabel));
                OnPropertyChanged(nameof(SelectButtonDisplayText));
                UpdateVisualBorder();
            }
        }
    }

    public string SelectButtonLabel => IsSelected ? "Viewing" : "Select";
    public string SelectButtonDisplayText => IsSelected ? "✓ Active" : "Select";

    public IBrush SemanticEdgeAccentBrush => EffectiveAlarmPriority switch
    {
        AlarmPriorityTier.HighRed => HighRedStrongBrush,
        AlarmPriorityTier.MediumYellow => MediumAmberStrongBrush,
        _ => NominalTealBrush
    };

    public BoxShadows CardBoxShadow => IsSelected ? SelectedCardShadows : DefaultCardShadows;

    public IBrush CardOuterHighlightBrush =>
        IsSelected && IsNominal
            ? SelectedHighlightBrush
            : AlarmBorderBrush;

    public IBrush AlarmBorderBrush
    {
        get => _alarmBorderBrush;
        private set
        {
            if (SetProperty(ref _alarmBorderBrush, value))
            {
                OnPropertyChanged(nameof(CardOuterHighlightBrush));
            }
        }
    }

    public Thickness AlarmBorderThickness
    {
        get => _alarmBorderThickness;
        private set => SetProperty(ref _alarmBorderThickness, value);
    }

    public IBrush AlarmBadgeBackgroundBrush
    {
        get => _alarmBadgeBackgroundBrush;
        private set => SetProperty(ref _alarmBadgeBackgroundBrush, value);
    }

    public IBrush AlarmBadgeBorderBrush
    {
        get => _alarmBadgeBorderBrush;
        private set => SetProperty(ref _alarmBadgeBorderBrush, value);
    }

    public IBrush AlarmBadgeForegroundBrush
    {
        get => _alarmBadgeForegroundBrush;
        private set => SetProperty(ref _alarmBadgeForegroundBrush, value);
    }

    public IBrush AlarmCardBackgroundBrush
    {
        get => _alarmCardBackgroundBrush;
        private set => SetProperty(ref _alarmCardBackgroundBrush, value);
    }

    public void ApplyFlashState(bool highFlashOn, bool mediumFlashOn)
    {
        switch (EffectiveAlarmPriority)
        {
            case AlarmPriorityTier.HighRed:
                IsFlashEmphasized = highFlashOn;
                AlarmBadgeBackgroundBrush = highFlashOn ? HighRedStrongBrush : HighRedSoftBgBrush;
                AlarmBadgeBorderBrush = HighRedStrongBrush;
                AlarmBadgeForegroundBrush = highFlashOn ? WhiteBrush : HighRedInkBrush;
                AlarmCardBackgroundBrush = highFlashOn ? HighRedCardTintBrush : WhiteBrush;
                break;

            case AlarmPriorityTier.MediumYellow:
                IsFlashEmphasized = mediumFlashOn;
                AlarmBadgeBackgroundBrush = mediumFlashOn ? MediumAmberStrongBrush : MediumAmberSoftBgBrush;
                AlarmBadgeBorderBrush = MediumAmberStrongBrush;
                AlarmBadgeForegroundBrush = mediumFlashOn ? WhiteBrush : MediumAmberInkBrush;
                AlarmCardBackgroundBrush = mediumFlashOn ? MediumAmberCardTintBrush : WhiteBrush;
                break;

            default:
                IsFlashEmphasized = false;
                AlarmBadgeBackgroundBrush = NominalTealSoftBgBrush;
                AlarmBadgeBorderBrush = NominalTealBorderBrush;
                AlarmBadgeForegroundBrush = NominalTealInkBrush;
                AlarmCardBackgroundBrush = WhiteBrush;
                break;
        }

        UpdateVisualBorder();
    }

    private void UpdateVisualBorder()
    {
        switch (EffectiveAlarmPriority)
        {
            case AlarmPriorityTier.HighRed:
                AlarmBorderBrush = IsFlashEmphasized ? HighRedStrongBrush : HighRedDimBorderBrush;
                AlarmBorderThickness = new Thickness(2);
                break;

            case AlarmPriorityTier.MediumYellow:
                AlarmBorderBrush = IsFlashEmphasized ? MediumAmberStrongBrush : MediumAmberDimBorderBrush;
                AlarmBorderThickness = new Thickness(1.5);
                break;

            default:
                AlarmBorderBrush = IsSelected ? SelectedHighlightBrush : SoftBorderBrush;
                AlarmBorderThickness = IsSelected ? new Thickness(1.5) : new Thickness(1);
                break;
        }
    }
}
