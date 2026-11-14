using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Services_Unit.Core.Formatting;
using Services_Unit.DecisionSupport.Contracts;
using Services_Unit.RiskPrediction.Contracts;

namespace NephroEdge.Desktop.Infrastructure;

/// <summary>
/// Shared clinical display formatter translating raw database enums, LOINC/rule codes,
/// and backend identifiers into calm, unambiguous clinician-facing language while
/// preserving ISMP typography and unit safety rules.
/// </summary>
public static class ClinicalDisplayFormatter
{
    private static readonly Regex PascalBoundaryRegex = new(
        @"(?<=[a-z0-9])([A-Z])",
        RegexOptions.Compiled);

    private static readonly Regex TrailingPointZeroRegex = new(
        @"(\b\d+)\.0+(?=\s*(?:[-–—/,)\]]|$|mmHg|mL/kg/h|mL/h|mL/min|bpm|kg|g/dL|mg/dL|mmol/L|mEq/L|L\b|mL\b|mg\b|mcg\b|units\b|%|min\b|h\b))",
        RegexOptions.Compiled);

    private static readonly Regex TrailingDecimalZeroRegex = new(
        @"(\b\d+\.\d*[1-9])0+(?=\s*(?:[-–—/,)\]]|$|mmHg|mL/kg/h|mL/h|mL/min|bpm|kg|g/dL|mg/dL|mmol/L|mEq/L|L\b|mL\b|mg\b|mcg\b|units\b|%|min\b|h\b))",
        RegexOptions.Compiled);

    private static readonly Regex SyncVersionMismatchRegex = new(
        @"SyncVersion mismatch:\s*bedside reported\s*(\d+),\s*expected\s*(\d+)\.?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AlreadyAckSyncVersionRegex = new(
        @"OrderHandshake\s+'([^']+)'\s+was already acknowledged at SyncVersion\s+(\d+);\s*reported\s+(\d+)\.?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RawSyncVersionWordRegex = new(
        @"\bSyncVersion\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RawOrderHandshakeWordRegex = new(
        @"\bOrderHandshake\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string FormatVascularAccess(string? accessType)
    {
        if (string.IsNullOrWhiteSpace(accessType))
        {
            return "Access Not Recorded";
        }

        string trimmed = accessType.Trim();
        return trimmed switch
        {
            "AV_Fistula" => "AV Fistula",
            "AV_Graft" => "AV Graft",
            "Central_Venous_Catheter" => "Central Venous Catheter",
            "CVC_Tunnelled" => "Tunnelled CVC",
            _ => SplitUnderscoresAndPascal(trimmed)
        };
    }

    public static string FormatAccessSite(string? accessSite)
    {
        if (string.IsNullOrWhiteSpace(accessSite))
        {
            return string.Empty;
        }

        string normalized = accessSite.Trim().Replace('_', ' ');
        return ToReadableSiteTitle(normalized);
    }

    public static string FormatSessionStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "Unknown";
        }

        string trimmed = status.Trim();
        return trimmed switch
        {
            "InProgress" => "In Progress",
            "Scheduled" => "Scheduled",
            "Completed" => "Completed",
            "Aborted" => "Aborted",
            _ => SplitUnderscoresAndPascal(trimmed)
        };
    }

    public static string FormatRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return "Unspecified Route";
        }

        string trimmed = route.Trim();
        return trimmed switch
        {
            "IV_Circuit" => "IV Circuit",
            "IV_Bolus" => "IV Bolus",
            "IV" => "IV",
            "PO" => "PO",
            "SC" => "SC",
            _ => SplitUnderscoresAndPascal(trimmed)
        };
    }

    public static string FormatDriverName(string? featureName)
    {
        if (string.IsNullOrWhiteSpace(featureName))
        {
            return "Clinical Indicator";
        }

        string trimmed = featureName.Trim();
        return trimmed switch
        {
            "LatestSbpMmHg" => "Latest Systolic BP",
            "DeltaSbpFromPreBaselineMmHg" => "SBP Drop from Pre-Dialysis",
            "DeltaMapFromPreBaselineMmHg" => "MAP Drop from Pre-Dialysis",
            "SbpLinearSlopeMmHgPerHour" => "BP Rate of Change",
            "EffectiveUfrMlPerKgPerHour" => "Ultrafiltration Rate",
            "HistoricalNadirMinSbpMmHg" => "10-Session Lowest Nadir BP",
            "PulsePressureMmHg" => "Pulse Pressure",
            _ => SplitUnderscoresAndPascal(trimmed)
        };
    }

    public static string FormatRuleLabel(string? ruleId)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
        {
            return "Clinical Watch";
        }

        string trimmed = ruleId.Trim();
        return trimmed switch
        {
            "IDH-FLYTHE-NADIR90" => "Low BP Alert (< 90 mmHg)",
            "IDH-KDOQI-DROP20" => "Rapid BP Drop (>= 20 mmHg)",
            "IDH-FLYTHE-NADIR100-SLOPE" => "Falling BP Trend (< 100 mmHg)",
            "IDH-KDOQI-UFR-CAP" => "High Fluid Removal Rate (> 13 mL/kg/h)",
            "IDH-PP-LOAD-INFLECTION" => "Wide Pulse Pressure Stress",
            _ => SplitUnderscoresAndPascal(trimmed.Replace('-', ' '))
        };
    }

    public static string FormatCdssStatus(OverallSafetyStatus status)
    {
        return status switch
        {
            OverallSafetyStatus.Approved => "Safe to Proceed",
            OverallSafetyStatus.WarningAdvisory => "Clinical Advisory",
            OverallSafetyStatus.BlockedContraindication => "Contraindication Blocked",
            _ => SplitUnderscoresAndPascal(status.ToString())
        };
    }

    public static string FormatCdssStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "Not Evaluated";
        }

        string trimmed = status.Trim();
        return trimmed switch
        {
            "Approved" or "SafeToProceed" => "Safe to Proceed",
            "WarningAdvisory" => "Clinical Advisory",
            "BlockedContraindication" => "Contraindication Blocked",
            _ => SplitUnderscoresAndPascal(trimmed)
        };
    }

    public static string FormatHandshakeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "Awaiting Nurse Ack";
        }

        string trimmed = status.Trim();
        return trimmed switch
        {
            "PENDING_ACK" => "Awaiting Nurse Ack",
            "CONFLICT_REQUIRES_CLINICIAN_ACK" => "Conflict — Review Needed",
            "ACKNOWLEDGED" => "Confirmed",
            _ => SplitUnderscoresAndPascal(trimmed)
        };
    }

    public static string FormatDirectiveType(string? directiveType)
    {
        if (string.IsNullOrWhiteSpace(directiveType))
        {
            return "Clinical Order";
        }

        string trimmed = directiveType.Trim();
        return trimmed switch
        {
            "DRY_WEIGHT_UPDATE" => "Dry Weight Update",
            "PRESCRIPTION_CHANGE" => "Prescription Change",
            "UF_ADJUSTMENT" => "UF Adjustment",
            "DIALYSATE_TEMP" => "Dialysate Temp",
            "BOLUS_HOLD" => "Bolus Hold",
            _ => ToTitleWords(trimmed.Replace('_', ' '))
        };
    }

    public static string FormatAbnormalityFlag(string? flag)
    {
        if (string.IsNullOrWhiteSpace(flag))
        {
            return "Normal";
        }

        string trimmed = flag.Trim();
        return trimmed switch
        {
            "CriticalHigh" => "Critical High",
            "CriticalLow" => "Critical Low",
            "High" => "High",
            "Low" => "Low",
            "Normal" => "Normal",
            _ => SplitUnderscoresAndPascal(trimmed)
        };
    }

    public static string FormatLabTrendDirection(string? direction)
    {
        if (string.IsNullOrWhiteSpace(direction))
        {
            return "Baseline (1 reading)";
        }

        string trimmed = direction.Trim();
        return trimmed switch
        {
            "InsufficientData" => "Baseline (1 reading)",
            "Rising" => "Rising",
            "Falling" => "Falling",
            "Stable" => "Stable",
            _ => SplitUnderscoresAndPascal(trimmed)
        };
    }

    public static string FormatSamplingPhase(string? phase)
    {
        if (string.IsNullOrWhiteSpace(phase))
        {
            return "Unspecified";
        }

        string trimmed = phase.Trim();
        return trimmed switch
        {
            "PreDialysis" => "Pre-Dialysis",
            "PostDialysis" => "Post-Dialysis",
            "MidDialysis" => "Mid-Dialysis",
            "Interdialytic" => "Interdialytic",
            _ => SplitUnderscoresAndPascal(trimmed)
        };
    }

    public static string FormatExecutionSource(RiskPredictionExecutionSource source)
    {
        return source switch
        {
            RiskPredictionExecutionSource.LocalDeterministic => "On-Device Clinical Engine",
            RiskPredictionExecutionSource.FallbackLocalDeterministic => "On-Device Fallback Engine",
            RiskPredictionExecutionSource.RemoteHeavyAi => "Cloud AI Engine",
            _ => SplitUnderscoresAndPascal(source.ToString())
        };
    }

    public static string FormatExecutionSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return "On-Device Clinical Engine";
        }

        string trimmed = source.Trim();
        return trimmed switch
        {
            "LocalDeterministic" => "On-Device Clinical Engine",
            "FallbackLocalDeterministic" => "On-Device Fallback Engine",
            "RemoteHeavyAi" => "Cloud AI Engine",
            _ => SplitUnderscoresAndPascal(trimmed)
        };
    }

    public static string FormatAlarmTier(AlarmPriorityTier tier)
    {
        return tier switch
        {
            AlarmPriorityTier.HighRed => "High Priority Alarm",
            AlarmPriorityTier.MediumYellow => "Medium Priority Warning",
            _ => "Nominal"
        };
    }

    public static string FormatAlarmTier(string? tier)
    {
        if (string.IsNullOrWhiteSpace(tier))
        {
            return "Nominal";
        }

        string trimmed = tier.Trim();
        return trimmed switch
        {
            "HighRed" => "High Priority Alarm",
            "MediumYellow" => "Medium Priority Warning",
            "None" => "Nominal",
            _ => SplitUnderscoresAndPascal(trimmed)
        };
    }

    public static string FormatVolumeMlOrL(double volumeMl)
    {
        if (!double.IsFinite(volumeMl) || volumeMl <= 0.0)
        {
            return "0 mL";
        }

        if (volumeMl >= 1000.0)
        {
            double liters = Math.Round(volumeMl / 1000.0, 2);
            return IsmpMedicationFormatter.FormatNumericDose(liters, "L");
        }

        return IsmpMedicationFormatter.FormatNumericDose(Math.Round(volumeMl, 0), "mL");
    }

    public static string FormatRateMlHr(double rateMlHr)
    {
        if (!double.IsFinite(rateMlHr) || rateMlHr <= 0.0)
        {
            return "0 mL/h";
        }

        return IsmpMedicationFormatter.FormatNumericDose(Math.Round(rateMlHr, 0), "mL/h");
    }

    public static string FormatCleanMeasurementText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string cleaned = TrailingPointZeroRegex.Replace(text.Trim(), "$1");
        return TrailingDecimalZeroRegex.Replace(cleaned, "$1");
    }

    public static string FormatSyncConflictSummary(string? rawReason)
    {
        if (string.IsNullOrWhiteSpace(rawReason))
        {
            return string.Empty;
        }

        string formatted = SyncVersionMismatchRegex.Replace(
            rawReason.Trim(),
            "Order version mismatch (bedside v$1 vs workstation v$2).");
        formatted = AlreadyAckSyncVersionRegex.Replace(
            formatted,
            "Order $1 was already confirmed at version $2 (bedside sent v$3).");
        formatted = RawSyncVersionWordRegex.Replace(formatted, "order version");
        formatted = RawOrderHandshakeWordRegex.Replace(formatted, "order directive");
        return FormatCleanMeasurementText(formatted);
    }

    public static string FormatHumanTimestamp(string? isoUtc)
    {
        if (string.IsNullOrWhiteSpace(isoUtc))
        {
            return "--";
        }

        if (DateTimeOffset.TryParse(
                isoUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset dto))
        {
            return dto.ToString("MMM dd · HH:mm 'UTC'", CultureInfo.InvariantCulture);
        }

        return isoUtc.Trim();
    }

    private static string SplitUnderscoresAndPascal(string value)
    {
        string withSpaces = value.Replace('_', ' ').Trim();
        return PascalBoundaryRegex.Replace(withSpaces, " $1");
    }

    private static string ToTitleWords(string text)
    {
        string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string word = parts[i];
            if (word.Length <= 2 && word.All(char.IsLetter))
            {
                parts[i] = word.ToUpperInvariant();
            }
            else
            {
                parts[i] = char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
            }
        }

        return string.Join(' ', parts);
    }

    private static string ToReadableSiteTitle(string text)
    {
        string[] tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i];
            if (token.Contains('-', StringComparison.Ordinal))
            {
                string[] sub = token.Split('-', StringSplitOptions.RemoveEmptyEntries);
                for (int j = 0; j < sub.Length; j++)
                {
                    sub[j] = CapitalizeSegment(sub[j]);
                }
                tokens[i] = string.Join('-', sub);
            }
            else
            {
                tokens[i] = CapitalizeSegment(token);
            }
        }

        return string.Join(' ', tokens);
    }

    private static string CapitalizeSegment(string segment)
    {
        if (segment.Length == 0)
        {
            return segment;
        }

        if (segment.Length <= 3 && segment.All(char.IsUpper))
        {
            return segment;
        }

        return char.ToUpperInvariant(segment[0]) + segment[1..];
    }
}
