using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using NephroEdge.Desktop.Infrastructure;
using NephroEdge.Desktop.ViewModels;

namespace NephroEdge.Desktop.Views;

/// <summary>
/// Code-behind for Clinical Safety, ADE, Dosing &amp; FDA Criterion 4 Rx Workbench.
/// </summary>
public partial class SafetyRxWorkbenchView : UserControl
{
    private static readonly IBrush CyanBg = new SolidColorBrush(Color.Parse("#ECFEFF"));
    private static readonly IBrush CyanBorder = new SolidColorBrush(Color.Parse("#0891B2"));
    private static readonly IBrush CyanFg = new SolidColorBrush(Color.Parse("#0E7490"));

    private static readonly IBrush AmberBg = new SolidColorBrush(Color.Parse("#FFFBEB"));
    private static readonly IBrush AmberBorder = new SolidColorBrush(Color.Parse("#D97706"));
    private static readonly IBrush AmberFg = new SolidColorBrush(Color.Parse("#92400E"));

    private static readonly IBrush TealBg = new SolidColorBrush(Color.Parse("#F0FDFA"));
    private static readonly IBrush TealBorder = new SolidColorBrush(Color.Parse("#0F766E"));
    private static readonly IBrush TealFg = new SolidColorBrush(Color.Parse("#115E59"));

    private static string HumanizeDriverText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return text
            .Replace("Nadir90", "Lowest SBP", StringComparison.Ordinal)
            .Replace("Nadir100", "Lowest SBP", StringComparison.Ordinal)
            .Replace("dMAP", "BP Drop", StringComparison.Ordinal);
    }

    public static readonly IValueConverter DriverNameConverter =
        new FuncValueConverter<string?, string>(raw =>
            HumanizeDriverText(ClinicalDisplayFormatter.FormatDriverName(raw)));

    public static readonly IValueConverter DriverValueConverter =
        new FuncValueConverter<string?, string>(raw =>
            HumanizeDriverText(SafetyRxWorkbenchViewModel.SanitizeIsmpText(raw)));

    public static readonly IValueConverter CleanMeasurementConverter =
        new FuncValueConverter<string?, string>(SafetyRxWorkbenchViewModel.SanitizeIsmpText);

    public static readonly IValueConverter ConflictSummaryConverter =
        new FuncValueConverter<string?, string>(raw =>
            SafetyRxWorkbenchViewModel.SanitizeIsmpText(
                ClinicalDisplayFormatter.FormatSyncConflictSummary(raw)));

    public static readonly IValueConverter HandshakeStatusConverter =
        new FuncValueConverter<string?, string>(ClinicalDisplayFormatter.FormatHandshakeStatus);

    public static readonly IValueConverter DirectiveTypeConverter =
        new FuncValueConverter<string?, string>(ClinicalDisplayFormatter.FormatDirectiveType);

    public static readonly IValueConverter CleanRouteDoseConverter =
        new FuncValueConverter<string?, string>(SafetyRxWorkbenchViewModel.FormatCleanRouteDoseText);

    public static readonly IValueConverter HumanTimestampConverter =
        new FuncValueConverter<string?, string>(ClinicalDisplayFormatter.FormatHumanTimestamp);

    public static readonly IValueConverter RxNormDisplayConverter =
        new FuncValueConverter<string?, string>(SafetyRxWorkbenchViewModel.FormatRxNormDisplay);

    public static readonly IValueConverter InteractingEntityConverter =
        new FuncValueConverter<string?, string>(SafetyRxWorkbenchViewModel.FormatInteractingEntity);

    public static readonly IValueConverter HandshakeBadgeBgConverter =
        new FuncValueConverter<string?, IBrush>(status => status switch
        {
            "CONFLICT_REQUIRES_CLINICIAN_ACK" => AmberBg,
            "ACKNOWLEDGED" => TealBg,
            _ => CyanBg
        });

    public static readonly IValueConverter HandshakeBadgeBorderConverter =
        new FuncValueConverter<string?, IBrush>(status => status switch
        {
            "CONFLICT_REQUIRES_CLINICIAN_ACK" => AmberBorder,
            "ACKNOWLEDGED" => TealBorder,
            _ => CyanBorder
        });

    public static readonly IValueConverter HandshakeBadgeFgConverter =
        new FuncValueConverter<string?, IBrush>(status => status switch
        {
            "CONFLICT_REQUIRES_CLINICIAN_ACK" => AmberFg,
            "ACKNOWLEDGED" => TealFg,
            _ => CyanFg
        });

    private SafetyRxWorkbenchViewModel? _attachedVm;

    public SafetyRxWorkbenchView()
    {
        InitializeComponent();
        DataContextChanged += OnViewDataContextChanged;
    }

    public void ScrollToTop()
    {
        var scrollViewer = this.FindControl<ScrollViewer>("SafetyRxScrollViewer");
        if (scrollViewer is not null)
        {
            scrollViewer.Offset = default;
        }
    }

    private void OnViewDataContextChanged(object? sender, EventArgs e)
    {
        if (_attachedVm is not null)
        {
            _attachedVm.PatientContextSwitched -= OnPatientContextSwitched;
            _attachedVm = null;
        }

        if (DataContext is SafetyRxWorkbenchViewModel nextVm)
        {
            _attachedVm = nextVm;
            nextVm.PatientContextSwitched += OnPatientContextSwitched;
        }
    }

    private void OnPatientContextSwitched(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ScrollToTop();
        }
        else
        {
            Dispatcher.UIThread.Post(ScrollToTop);
        }
    }

    private void OnAdeInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is SafetyRxWorkbenchViewModel vm && vm.EvaluateSafetyCommand.CanExecute(null))
        {
            vm.EvaluateSafetyCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnDryWeightKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is SafetyRxWorkbenchViewModel vm && vm.SaveDryWeightCommand.CanExecute(null))
        {
            vm.SaveDryWeightCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnSafetyPillPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ExpandAndScrollToClinicalReasoning();
        e.Handled = true;
    }

    private void OnOpenClinicalReasoningClick(object? sender, RoutedEventArgs e)
    {
        ExpandAndScrollToClinicalReasoning();
        e.Handled = true;
    }

    private void OnOpenMedicationCheckClick(object? sender, RoutedEventArgs e)
    {
        var expander = this.FindControl<Expander>("MedicationAnemiaExpander");
        if (expander is not null)
        {
            expander.IsExpanded = true;
        }

        Dispatcher.UIThread.Post(
            () => ScrollControlIntoView("AdeMedicationCard"),
            DispatcherPriority.Loaded);
        e.Handled = true;
    }

    private void ExpandAndScrollToClinicalReasoning()
    {
        var expander = this.FindControl<Expander>("ClinicalReasoningExpander");
        if (expander is not null)
        {
            expander.IsExpanded = true;
        }

        Dispatcher.UIThread.Post(
            () => ScrollControlIntoView("Criterion4Card"),
            DispatcherPriority.Loaded);
    }

    private void ScrollControlIntoView(string controlName)
    {
        var scrollViewer = this.FindControl<ScrollViewer>("SafetyRxScrollViewer");
        var targetCard = this.FindControl<Border>(controlName);
        if (scrollViewer is null || targetCard is null)
        {
            return;
        }

        var relativePos = targetCard.TranslatePoint(new Point(0, 0), scrollViewer);
        if (relativePos.HasValue)
        {
            double targetY = Math.Max(0.0, scrollViewer.Offset.Y + relativePos.Value.Y - 10.0);
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, targetY);
        }
        else
        {
            targetCard.BringIntoView();
        }
    }
}
