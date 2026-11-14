using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace NephroEdge.Desktop.Infrastructure;

/// <summary>
/// Avalonia IValueConverter delegating raw DB/enum strings in XAML DataTemplates to
/// <see cref="ClinicalDisplayFormatter"/> while leaving underlying DTO collections intact.
/// </summary>
public sealed class ClinicalDisplayConverter : IValueConverter
{
    private static readonly IBrush AmberBgBrush = new SolidColorBrush(Color.Parse("#FEF3C7")).ToImmutable();
    private static readonly IBrush AmberBorderBrush = new SolidColorBrush(Color.Parse("#D97706")).ToImmutable();
    private static readonly IBrush AmberFgBrush = new SolidColorBrush(Color.Parse("#92400E")).ToImmutable();

    private static readonly IBrush CyanBgBrush = new SolidColorBrush(Color.Parse("#ECFEFF")).ToImmutable();
    private static readonly IBrush CyanBorderBrush = new SolidColorBrush(Color.Parse("#0891B2")).ToImmutable();
    private static readonly IBrush CyanFgBrush = new SolidColorBrush(Color.Parse("#0E7490")).ToImmutable();

    private static readonly IBrush TealBgBrush = new SolidColorBrush(Color.Parse("#F0FDFA")).ToImmutable();
    private static readonly IBrush TealBorderBrush = new SolidColorBrush(Color.Parse("#0F766E")).ToImmutable();
    private static readonly IBrush TealFgBrush = new SolidColorBrush(Color.Parse("#115E59")).ToImmutable();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string? raw = value?.ToString();
        string mode = parameter?.ToString() ?? string.Empty;

        return mode switch
        {
            "HandshakeStatus" => ClinicalDisplayFormatter.FormatHandshakeStatus(raw),
            "HandshakeStatusBg" => ResolveHandshakeBrush(raw, amber: AmberBgBrush, teal: TealBgBrush, cyan: CyanBgBrush),
            "HandshakeStatusBorder" => ResolveHandshakeBrush(raw, amber: AmberBorderBrush, teal: TealBorderBrush, cyan: CyanBorderBrush),
            "HandshakeStatusFg" => ResolveHandshakeBrush(raw, amber: AmberFgBrush, teal: TealFgBrush, cyan: CyanFgBrush),
            "ConflictSummary" => ClinicalDisplayFormatter.FormatSyncConflictSummary(raw),
            "DirectiveType" => ClinicalDisplayFormatter.FormatDirectiveType(raw),
            "SessionStatus" => ClinicalDisplayFormatter.FormatSessionStatus(raw),
            "VascularAccess" => ClinicalDisplayFormatter.FormatVascularAccess(raw),
            "Route" => ClinicalDisplayFormatter.FormatRoute(raw),
            "Timestamp" => ClinicalDisplayFormatter.FormatHumanTimestamp(raw),
            "OutboxAggregate" => FormatOutboxAggregate(raw),
            "OutboxOperation" => FormatOutboxOperation(raw),
            "OutboxStatus" => FormatOutboxStatus(raw),
            _ => raw ?? string.Empty
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("ClinicalDisplayConverter is one-way display formatting only.");
    }

    private static string FormatOutboxAggregate(string? aggregateType)
    {
        if (string.IsNullOrWhiteSpace(aggregateType))
        {
            return "Clinical Record";
        }

        return aggregateType.Trim() switch
        {
            "PatientDryWeight" or "PatientDryWeightHistory" => "Dry Weight",
            "DialysisPrescription" or "DialysisPrescriptions" => "Prescription",
            "OrderHandshake" or "OrderHandshakes" => "Order Handshake",
            "DialysisSession" or "DialysisSessions" => "Dialysis Session",
            "LabResult" or "LabResults" => "Lab Result",
            _ => aggregateType.Replace('_', ' ').Trim()
        };
    }

    private static string FormatOutboxOperation(string? op)
    {
        if (string.IsNullOrWhiteSpace(op))
        {
            return "Sync";
        }

        return op.Trim().ToUpperInvariant() switch
        {
            "INSERT" => "Created",
            "UPDATE" => "Updated",
            "UPSERT" => "Synced",
            "DELETE" => "Archived",
            _ => op.Replace('_', ' ').Trim()
        };
    }

    private static string FormatOutboxStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "Pending";
        }

        return status.Trim().ToUpperInvariant() switch
        {
            "PENDING" => "Pending Sync",
            "SENT" or "SYNCED" => "Synced",
            "FAILED" => "Retry Queued",
            _ => status.Replace('_', ' ').Trim()
        };
    }

    private static IBrush ResolveHandshakeBrush(string? status, IBrush amber, IBrush teal, IBrush cyan)
    {
        string normalized = status?.Trim() ?? string.Empty;
        if (string.Equals(normalized, "CONFLICT_REQUIRES_CLINICIAN_ACK", StringComparison.Ordinal))
        {
            return amber;
        }

        if (string.Equals(normalized, "ACKNOWLEDGED", StringComparison.Ordinal))
        {
            return teal;
        }

        return cyan;
    }
}
