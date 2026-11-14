using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using NephroEdge.Desktop.Infrastructure;
using NephroEdge.Desktop.ViewModels;
using Services_Unit.Analytics.Contracts;

namespace NephroEdge.Desktop.Views;

/// <summary>
/// Code-behind for Patient Longitudinal Analytics.
/// </summary>
public partial class AnalyticsView : UserControl
{
    private static readonly IBrush SlateInkBrush = new SolidColorBrush(Color.Parse("#0F172A"));
    private static readonly IBrush CrimsonAlertBrush = new SolidColorBrush(Color.Parse("#D32F2F"));
    private static readonly IBrush CrimsonTextBrush = new SolidColorBrush(Color.Parse("#B91C1C"));
    private static readonly IBrush CrimsonSoftBgBrush = new SolidColorBrush(Color.Parse("#FEE2E2"));
    private static readonly IBrush AmberAlertBrush = new SolidColorBrush(Color.Parse("#D97706"));
    private static readonly IBrush AmberTextBrush = new SolidColorBrush(Color.Parse("#B45309"));
    private static readonly IBrush AmberSoftBgBrush = new SolidColorBrush(Color.Parse("#FEF3C7"));
    private static readonly IBrush SkyBaselineBrush = new SolidColorBrush(Color.Parse("#0284C7"));
    private static readonly IBrush SkyBaselineTextBrush = new SolidColorBrush(Color.Parse("#0369A1"));
    private static readonly IBrush SkySoftBgBrush = new SolidColorBrush(Color.Parse("#E0F2FE"));
    private static readonly IBrush TealSafeBrush = new SolidColorBrush(Color.Parse("#0F766E"));
    private static readonly IBrush TealSoftBgBrush = new SolidColorBrush(Color.Parse("#CCFBF1"));

    public static readonly IValueConverter LoincFilterLabelConverter =
        new FuncValueConverter<string?, string>(AnalyticsViewModel.FormatLoincFilterLabel);

    public static readonly IValueConverter TrendDirectionConverter =
        new FuncValueConverter<string?, string>(ClinicalDisplayFormatter.FormatLabTrendDirection);

    public static readonly IValueConverter HumanTimestampConverter =
        new FuncValueConverter<string?, string>(ClinicalDisplayFormatter.FormatHumanTimestamp);

    public static readonly IValueConverter NadirSeverityBrushConverter =
        new FuncValueConverter<string?, IBrush>(level => level switch
        {
            "Critical" => CrimsonAlertBrush,
            "Borderline" => AmberTextBrush,
            _ => SlateInkBrush
        });

    public static readonly IValueConverter KtVSeverityBrushConverter =
        new FuncValueConverter<string?, IBrush>(level => level switch
        {
            "Low" => AmberTextBrush,
            _ => TealSafeBrush
        });

    public static readonly IValueConverter IncidentCountBrushConverter =
        new FuncValueConverter<int?, IBrush>(count => count.GetValueOrDefault() > 0 ? AmberTextBrush : SlateInkBrush);

    public static readonly IValueConverter InadequateKtVBrushConverter =
        new FuncValueConverter<int?, IBrush>(count => count.GetValueOrDefault() > 0 ? CrimsonAlertBrush : SlateInkBrush);

    public static readonly IValueConverter AnalyteDotBrushConverter =
        new FuncValueConverter<LabSeriesTrend?, IBrush>(t => ClassifyAnalyteState(t).Dot);

    public static readonly IValueConverter AnalyteValueBrushConverter =
        new FuncValueConverter<LabSeriesTrend?, IBrush>(t => ClassifyAnalyteState(t).ValueBrush);

    public static readonly IValueConverter AnalyteStatusLabelConverter =
        new FuncValueConverter<LabSeriesTrend?, string>(t => ClassifyAnalyteState(t).Label);

    public static readonly IValueConverter AnalytePillBgConverter =
        new FuncValueConverter<LabSeriesTrend?, IBrush>(t => ClassifyAnalyteState(t).PillBg);

    public static readonly IValueConverter AnalytePillFgConverter =
        new FuncValueConverter<LabSeriesTrend?, IBrush>(t => ClassifyAnalyteState(t).PillFg);

    private static (string Label, IBrush Dot, IBrush ValueBrush, IBrush PillBg, IBrush PillFg) ClassifyAnalyteState(LabSeriesTrend? trend)
    {
        if (trend is null)
        {
            return ("Baseline", SkyBaselineBrush, SlateInkBrush, SkySoftBgBrush, SkyBaselineTextBrush);
        }

        var latestPoint = trend.Points.LastOrDefault();
        string? normalizedFlag = latestPoint?.AbnormalityFlag?
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToUpperInvariant();
        bool isHighOrLow = normalizedFlag is "HIGH" or "CRITICALHIGH" or "LOW" or "CRITICALLOW"
                           || (trend.LatestValue is double val && (val > trend.NormalRangeHigh || val < trend.NormalRangeLow));

        if (trend.HasPanicPoint || normalizedFlag is "CRITICALHIGH" or "CRITICALLOW")
        {
            return ("Panic", CrimsonAlertBrush, CrimsonTextBrush, CrimsonSoftBgBrush, CrimsonTextBrush);
        }

        if (isHighOrLow)
        {
            bool isLow = normalizedFlag is "LOW" or "CRITICALLOW" || (trend.LatestValue is double lv && lv < trend.NormalRangeLow);
            return (isLow ? "Low" : "High", AmberAlertBrush, AmberTextBrush, AmberSoftBgBrush, AmberTextBrush);
        }

        if (!trend.LatestDelta.HasValue || trend.Points.Count <= 1)
        {
            return ("Baseline", SkyBaselineBrush, SlateInkBrush, SkySoftBgBrush, SkyBaselineTextBrush);
        }

        return ("Normal", TealSafeBrush, SlateInkBrush, TealSoftBgBrush, TealSafeBrush);
    }

    private AnalyticsViewModel? _attachedVm;

    public AnalyticsView()
    {
        InitializeComponent();
        DataContextChanged += OnViewDataContextChanged;
    }

    public void ScrollToTop()
    {
        var scrollViewer = this.FindControl<ScrollViewer>("AnalyticsScrollViewer");
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

        if (DataContext is AnalyticsViewModel nextVm)
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

    private void OnAddLabValueKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is AnalyticsViewModel vm && vm.SaveLabResultCommand.CanExecute(null))
        {
            vm.SaveLabResultCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnKtVInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is AnalyticsViewModel vm && vm.ComputeKtVCommand.CanExecute(null))
        {
            vm.ComputeKtVCommand.Execute(null);
            e.Handled = true;
        }
    }
}
