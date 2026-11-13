using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Media;
using NephroEdge.Desktop.Infrastructure;
using NephroEdge.Desktop.ViewModels;

namespace NephroEdge.Desktop.Views;

/// <summary>
/// Code-behind for Workspace 4 (Bedside Order Handshake Review &amp; Sync Bridge Diagnostics).
/// Resets scroll position on view activation and supports Ctrl+Enter quick conflict resolution.
/// </summary>
public partial class SyncMonitorView : UserControl
{
    private static readonly IBrush ConfirmedBg = new SolidColorBrush(Color.Parse("#F0FDFA"));
    private static readonly IBrush ConfirmedBorder = new SolidColorBrush(Color.Parse("#0F766E"));
    private static readonly IBrush ConfirmedFg = new SolidColorBrush(Color.Parse("#115E59"));

    private static readonly IBrush WaitingBg = new SolidColorBrush(Color.Parse("#ECFEFF"));
    private static readonly IBrush WaitingBorder = new SolidColorBrush(Color.Parse("#0891B2"));
    private static readonly IBrush WaitingFg = new SolidColorBrush(Color.Parse("#0E7490"));

    private static readonly IBrush ReviewBg = new SolidColorBrush(Color.Parse("#FEF3C7"));
    private static readonly IBrush ReviewBorder = new SolidColorBrush(Color.Parse("#D97706"));
    private static readonly IBrush ReviewFg = new SolidColorBrush(Color.Parse("#92400E"));

    public static readonly IValueConverter HandshakePillLabelConverter =
        new FuncValueConverter<string?, string>(status => status switch
        {
            "ACKNOWLEDGED" => "✓ Confirmed",
            "PENDING_ACK" => "⏳ Waiting",
            "CONFLICT_REQUIRES_CLINICIAN_ACK" => "◆ Needs Review",
            _ => ClinicalDisplayFormatter.FormatHandshakeStatus(status)
        });

    public static readonly IValueConverter HandshakeBadgeBgConverter =
        new FuncValueConverter<string?, IBrush>(status => status switch
        {
            "ACKNOWLEDGED" => ConfirmedBg,
            "CONFLICT_REQUIRES_CLINICIAN_ACK" => ReviewBg,
            _ => WaitingBg
        });

    public static readonly IValueConverter HandshakeBadgeBorderConverter =
        new FuncValueConverter<string?, IBrush>(status => status switch
        {
            "ACKNOWLEDGED" => ConfirmedBorder,
            "CONFLICT_REQUIRES_CLINICIAN_ACK" => ReviewBorder,
            _ => WaitingBorder
        });

    public static readonly IValueConverter HandshakeBadgeFgConverter =
        new FuncValueConverter<string?, IBrush>(status => status switch
        {
            "ACKNOWLEDGED" => ConfirmedFg,
            "CONFLICT_REQUIRES_CLINICIAN_ACK" => ReviewFg,
            _ => WaitingFg
        });

    public static readonly IValueConverter DirectiveTypeConverter =
        new FuncValueConverter<string?, string>(ClinicalDisplayFormatter.FormatDirectiveType);

    public static readonly IValueConverter CleanRouteDoseConverter =
        new FuncValueConverter<string?, string>(raw =>
            ClinicalDisplayFormatter.FormatCleanMeasurementText(ClinicalDisplayFormatter.FormatRoute(raw)));

    public static readonly IValueConverter HumanTimestampConverter =
        new FuncValueConverter<string?, string>(ClinicalDisplayFormatter.FormatHumanTimestamp);

    public static readonly IValueConverter ConflictReasonSummaryConverter =
        new FuncValueConverter<string?, string>(ClinicalDisplayFormatter.FormatSyncConflictSummary);

    public static readonly IMultiValueConverter PatientDisplayMultiConverter =
        new FuncMultiValueConverter<object?, string>(values =>
        {
            object?[] list = values.ToArray();
            if (list.Length >= 3 && list[2] is SyncMonitorViewModel vm)
            {
                return vm.ResolvePatientNameAndMrn(list[0] as string);
            }

            return list.Length > 0 ? list[0]?.ToString() ?? string.Empty : "Unassigned Patient";
        });

    public SyncMonitorView()
    {
        InitializeComponent();

        ResolutionNotesTextBox.KeyDown += OnResolutionNotesKeyDown;
    }

    public void ScrollToTop()
    {
        SyncMonitorScrollViewer.Offset = new Vector(0, 0);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty && change.GetNewValue<bool>())
        {
            ScrollToTop();
        }
    }

    private void OnResolutionNotesKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter &&
            e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
            DataContext is SyncMonitorViewModel vm &&
            vm.ResolveConflictCommand.CanExecute(null))
        {
            vm.ResolveConflictCommand.Execute(null);
            e.Handled = true;
        }
    }
}
