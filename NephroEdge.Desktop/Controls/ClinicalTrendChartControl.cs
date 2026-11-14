using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;

namespace NephroEdge.Desktop.Controls;

/// <summary>
/// Interactive native Skia-backed Avalonia trend chart control rendering longitudinal laboratory trajectories
/// and intradialytic blood pressure series with reference bands, panic bounds, hover crosshair tooltips,
/// and clickable legend toggles.
/// </summary>
public sealed class ClinicalTrendChartControl : Control, ICustomHitTest
{
    private static readonly IBrush CardBackgroundBrush = new SolidColorBrush(Color.Parse("#FFFFFF"));
    private static readonly IBrush PlotBackgroundBrush = new SolidColorBrush(Color.Parse("#F8FAFC"));
    private static readonly IBrush BorderBrushCard = new SolidColorBrush(Color.Parse("#CBD5E1"));
    private static readonly IBrush GridLineBrush = new SolidColorBrush(Color.Parse("#E2E8F0"));
    private static readonly IBrush InkBrush = new SolidColorBrush(Color.Parse("#0F172A"));
    private static readonly IBrush MutedInkBrush = new SolidColorBrush(Color.Parse("#475569"));
    private static readonly IBrush DimmedInkBrush = new SolidColorBrush(Color.Parse("#94A3B8"));
    private static readonly IBrush PrimarySeriesBrush = new SolidColorBrush(Color.Parse("#0284C7"));
    private static readonly IBrush SecondarySeriesBrush = new SolidColorBrush(Color.Parse("#0F766E"));
    private static readonly IBrush WarningAmberBrush = new SolidColorBrush(Color.Parse("#D97706"));
    private static readonly IBrush PanicRedBrush = new SolidColorBrush(Color.Parse("#DC2626"));
    private static readonly IBrush PanicHaloBrush = new SolidColorBrush(Color.FromArgb(64, 220, 38, 38));
    private static readonly IBrush HoverHaloBrush = new SolidColorBrush(Color.FromArgb(46, 2, 132, 199));
    private static readonly IBrush NormalBandFillBrush = new SolidColorBrush(Color.FromArgb(26, 15, 118, 110));
    private static readonly IBrush NormalBandStrokeBrush = new SolidColorBrush(Color.FromArgb(145, 15, 118, 110));
    private static readonly IBrush DotRingBrush = new SolidColorBrush(Color.Parse("#FFFFFF"));
    private static readonly IBrush LabelBackdropBrush = new SolidColorBrush(Color.FromArgb(234, 255, 255, 255));
    private static readonly IBrush ThresholdPillBgBrush = new SolidColorBrush(Color.FromArgb(242, 255, 255, 255));
    private static readonly IBrush ThresholdPillBorderBrush = new SolidColorBrush(Color.FromArgb(140, 252, 165, 165));
    private static readonly IPen ThresholdPillBorderPen = new Pen(ThresholdPillBorderBrush, 0.9);
    private static readonly IBrush LegendActiveBgBrush = new SolidColorBrush(Color.Parse("#F8FAFC"));
    private static readonly IBrush LegendHiddenBgBrush = new SolidColorBrush(Color.Parse("#F1F5F9"));
    private static readonly IBrush LegendHoverBorderBrush = new SolidColorBrush(Color.Parse("#0284C7"));
    private static readonly IBrush TooltipBgBrush = new SolidColorBrush(Color.FromArgb(240, 15, 23, 42));
    private static readonly IBrush TooltipBorderBrush = new SolidColorBrush(Color.Parse("#334155"));
    private static readonly IBrush TooltipHeaderBrush = new SolidColorBrush(Color.Parse("#F8FAFC"));
    private static readonly IBrush TooltipSubtextBrush = new SolidColorBrush(Color.Parse("#CBD5E1"));
    private static readonly IBrush TooltipPanicBrush = new SolidColorBrush(Color.Parse("#FCA5A5"));
    private static readonly IBrush TooltipAmberBrush = new SolidColorBrush(Color.Parse("#FCD34D"));
    private static readonly IBrush CrosshairBrush = new SolidColorBrush(Color.FromArgb(150, 100, 116, 139));

    private static readonly IPen CardBorderPen = new Pen(BorderBrushCard, 1.0);
    private static readonly IPen GridPen = new Pen(GridLineBrush, 1.0);
    private static readonly IPen EmptyDashedPen = new Pen(BorderBrushCard, 1.0, dashStyle: new DashStyle(new[] { 5.0, 4.0 }, 0));
    private static readonly IPen NormalRangePen = new Pen(NormalBandStrokeBrush, 1.0, dashStyle: new DashStyle(new[] { 4.0, 4.0 }, 0));
    private static readonly IPen PanicLinePen = new Pen(PanicRedBrush, 1.3, dashStyle: new DashStyle(new[] { 6.0, 4.0 }, 0));
    private static readonly IPen PrimaryLinePen = new Pen(PrimarySeriesBrush, 2.2);
    private static readonly IPen SecondaryLinePen = new Pen(SecondarySeriesBrush, 2.0);
    private static readonly IPen DotRingPen = new Pen(DotRingBrush, 1.5);
    private static readonly IPen HoverRingPen = new Pen(InkBrush, 1.6);
    private static readonly IPen LegendBorderPen = new Pen(BorderBrushCard, 1.0);
    private static readonly IPen LegendHoverPen = new Pen(LegendHoverBorderBrush, 1.3);
    private static readonly IPen TooltipBorderPen = new Pen(TooltipBorderBrush, 1.0);
    private static readonly IPen CrosshairPen = new Pen(CrosshairBrush, 1.0, dashStyle: new DashStyle(new[] { 3.0, 3.0 }, 0));

    private static readonly Typeface HeadingTypeface = new(Typeface.Default.FontFamily, FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface BodyTypeface = new(Typeface.Default.FontFamily, FontStyle.Normal, FontWeight.Normal);
    private static readonly Typeface MonoTypeface = new(new FontFamily("Consolas, JetBrains Mono, monospace"), FontStyle.Normal, FontWeight.SemiBold);

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<ClinicalTrendChartControl, string>(nameof(Title), "Clinical Trend");

    public static readonly StyledProperty<string> UnitLabelProperty =
        AvaloniaProperty.Register<ClinicalTrendChartControl, string>(nameof(UnitLabel), string.Empty);

    public static readonly StyledProperty<IReadOnlyList<ChartPointItem>?> SeriesPointsProperty =
        AvaloniaProperty.Register<ClinicalTrendChartControl, IReadOnlyList<ChartPointItem>?>(nameof(SeriesPoints));

    public static readonly StyledProperty<IReadOnlyList<ChartPointItem>?> SecondarySeriesPointsProperty =
        AvaloniaProperty.Register<ClinicalTrendChartControl, IReadOnlyList<ChartPointItem>?>(nameof(SecondarySeriesPoints));

    public static readonly StyledProperty<string> PrimarySeriesNameProperty =
        AvaloniaProperty.Register<ClinicalTrendChartControl, string>(nameof(PrimarySeriesName), "Primary");

    public static readonly StyledProperty<string> SecondarySeriesNameProperty =
        AvaloniaProperty.Register<ClinicalTrendChartControl, string>(nameof(SecondarySeriesName), "Secondary");

    public static readonly StyledProperty<double?> NormalLowProperty =
        AvaloniaProperty.Register<ClinicalTrendChartControl, double?>(nameof(NormalLow));

    public static readonly StyledProperty<double?> NormalHighProperty =
        AvaloniaProperty.Register<ClinicalTrendChartControl, double?>(nameof(NormalHigh));

    public static readonly StyledProperty<double?> PanicLowProperty =
        AvaloniaProperty.Register<ClinicalTrendChartControl, double?>(nameof(PanicLow));

    public static readonly StyledProperty<double?> PanicHighProperty =
        AvaloniaProperty.Register<ClinicalTrendChartControl, double?>(nameof(PanicHigh));

    public static readonly StyledProperty<double?> CriticalThresholdLineProperty =
        AvaloniaProperty.Register<ClinicalTrendChartControl, double?>(nameof(CriticalThresholdLine));

    public static readonly StyledProperty<string> CriticalThresholdLabelProperty =
        AvaloniaProperty.Register<ClinicalTrendChartControl, string>(nameof(CriticalThresholdLabel), "Safety Threshold");

    public static readonly StyledProperty<string> EmptyStateMessageProperty =
        AvaloniaProperty.Register<ClinicalTrendChartControl, string>(
            nameof(EmptyStateMessage),
            "No longitudinal measurements recorded yet for this patient.");

    public static readonly StyledProperty<bool> IsPrimarySeriesVisibleProperty =
        AvaloniaProperty.Register<ClinicalTrendChartControl, bool>(nameof(IsPrimarySeriesVisible), true);

    public static readonly StyledProperty<bool> IsSecondarySeriesVisibleProperty =
        AvaloniaProperty.Register<ClinicalTrendChartControl, bool>(nameof(IsSecondarySeriesVisible), true);

    private Rect _primaryLegendRect;
    private Rect _secondaryLegendRect;
    private Rect _lastPlotRect;
    private Rect _lastTooltipRect;
    private int? _hoveredSlotIndex;
    private int? _hoveredLegendIndex;
    private string? _lastTooltipSummary;

    static ClinicalTrendChartControl()
    {
        AffectsRender<ClinicalTrendChartControl>(
            TitleProperty,
            UnitLabelProperty,
            SeriesPointsProperty,
            SecondarySeriesPointsProperty,
            PrimarySeriesNameProperty,
            SecondarySeriesNameProperty,
            NormalLowProperty,
            NormalHighProperty,
            PanicLowProperty,
            PanicHighProperty,
            CriticalThresholdLineProperty,
            CriticalThresholdLabelProperty,
            EmptyStateMessageProperty,
            IsPrimarySeriesVisibleProperty,
            IsSecondarySeriesVisibleProperty);

        AffectsMeasure<ClinicalTrendChartControl>(
            SeriesPointsProperty,
            SecondarySeriesPointsProperty);
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string UnitLabel
    {
        get => GetValue(UnitLabelProperty);
        set => SetValue(UnitLabelProperty, value);
    }

    public IReadOnlyList<ChartPointItem>? SeriesPoints
    {
        get => GetValue(SeriesPointsProperty);
        set => SetValue(SeriesPointsProperty, value);
    }

    public IReadOnlyList<ChartPointItem>? SecondarySeriesPoints
    {
        get => GetValue(SecondarySeriesPointsProperty);
        set => SetValue(SecondarySeriesPointsProperty, value);
    }

    public string PrimarySeriesName
    {
        get => GetValue(PrimarySeriesNameProperty);
        set => SetValue(PrimarySeriesNameProperty, value);
    }

    public string SecondarySeriesName
    {
        get => GetValue(SecondarySeriesNameProperty);
        set => SetValue(SecondarySeriesNameProperty, value);
    }

    public double? NormalLow
    {
        get => GetValue(NormalLowProperty);
        set => SetValue(NormalLowProperty, value);
    }

    public double? NormalHigh
    {
        get => GetValue(NormalHighProperty);
        set => SetValue(NormalHighProperty, value);
    }

    public double? PanicLow
    {
        get => GetValue(PanicLowProperty);
        set => SetValue(PanicLowProperty, value);
    }

    public double? PanicHigh
    {
        get => GetValue(PanicHighProperty);
        set => SetValue(PanicHighProperty, value);
    }

    public double? CriticalThresholdLine
    {
        get => GetValue(CriticalThresholdLineProperty);
        set => SetValue(CriticalThresholdLineProperty, value);
    }

    public string CriticalThresholdLabel
    {
        get => GetValue(CriticalThresholdLabelProperty);
        set => SetValue(CriticalThresholdLabelProperty, value);
    }

    public string EmptyStateMessage
    {
        get => GetValue(EmptyStateMessageProperty);
        set => SetValue(EmptyStateMessageProperty, value);
    }

    public bool IsPrimarySeriesVisible
    {
        get => GetValue(IsPrimarySeriesVisibleProperty);
        set => SetValue(IsPrimarySeriesVisibleProperty, value);
    }

    public bool IsSecondarySeriesVisible
    {
        get => GetValue(IsSecondarySeriesVisibleProperty);
        set => SetValue(IsSecondarySeriesVisibleProperty, value);
    }

    public int? HoveredSlotIndex => _hoveredSlotIndex;

    public bool IsTooltipVisible => _hoveredSlotIndex.HasValue && !string.IsNullOrEmpty(_lastTooltipSummary);

    public string? LastTooltipSummary => _lastTooltipSummary;

    public Rect PrimaryLegendRect => _primaryLegendRect;

    public Rect SecondaryLegendRect => _secondaryLegendRect;

    public Rect PlotBoundsRect => _lastPlotRect;

    public Rect LastTooltipRect => _lastTooltipRect;

    public bool HitTest(Point point)
    {
        return point.X >= 0 && point.Y >= 0 && point.X <= Bounds.Width && point.Y <= Bounds.Height;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width) && availableSize.Width > 0
            ? Math.Max(260, availableSize.Width)
            : 520;
        double height = double.IsFinite(availableSize.Height) && availableSize.Height > 0
            ? Math.Clamp(availableSize.Height, 180, 320)
            : 220;
        return new Size(width, height);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        Point pos = e.GetPosition(this);
        EnsureLegendRectsComputed();

        int? nextLegendHover = null;
        if (_primaryLegendRect.Width > 0 && _primaryLegendRect.Contains(pos))
        {
            nextLegendHover = 0;
        }
        else if (_secondaryLegendRect.Width > 0 && _secondaryLegendRect.Contains(pos))
        {
            nextLegendHover = 1;
        }

        if (nextLegendHover.HasValue)
        {
            bool changed = _hoveredLegendIndex != nextLegendHover || _hoveredSlotIndex.HasValue;
            _hoveredLegendIndex = nextLegendHover;
            _hoveredSlotIndex = null;
            _lastTooltipSummary = null;
            Cursor = new Cursor(StandardCursorType.Hand);
            if (changed)
            {
                InvalidateVisual();
            }
            return;
        }

        var primary = FilterValidPoints(SeriesPoints);
        var secondary = FilterValidPoints(SecondarySeriesPoints);
        int slotCount = Math.Max(primary.Count, secondary.Count);
        bool anyVisible = (IsPrimarySeriesVisible && primary.Count > 0) || (IsSecondarySeriesVisible && secondary.Count > 0);
        Rect plotRect = ComputePlotRect(Bounds.Width, Bounds.Height);

        int? nextSlot = null;
        if (anyVisible && slotCount > 0 && plotRect.Width >= 40 && plotRect.Height >= 40 &&
            pos.X >= plotRect.X - 6.0 && pos.X <= plotRect.Right + 6.0 &&
            pos.Y >= plotRect.Y - 4.0 && pos.Y <= plotRect.Bottom + 4.0)
        {
            int bestIdx = 0;
            double bestDist = double.PositiveInfinity;
            for (int i = 0; i < slotCount; i++)
            {
                double x = IndexToX(i, slotCount, plotRect);
                double dist = Math.Abs(pos.X - x);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIdx = i;
                }
            }

            nextSlot = bestIdx;
        }

        Cursor = nextSlot.HasValue ? new Cursor(StandardCursorType.Cross) : Cursor.Default;

        if (_hoveredSlotIndex != nextSlot || _hoveredLegendIndex.HasValue)
        {
            _hoveredLegendIndex = null;
            _hoveredSlotIndex = nextSlot;
            if (!nextSlot.HasValue)
            {
                _lastTooltipSummary = null;
            }
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (_hoveredSlotIndex.HasValue || _hoveredLegendIndex.HasValue || _lastTooltipSummary is not null)
        {
            _hoveredSlotIndex = null;
            _hoveredLegendIndex = null;
            _lastTooltipSummary = null;
            Cursor = Cursor.Default;
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        Point pos = e.GetPosition(this);
        EnsureLegendRectsComputed();

        if (_primaryLegendRect.Width > 0 && _primaryLegendRect.Contains(pos))
        {
            IsPrimarySeriesVisible = !IsPrimarySeriesVisible;
            _hoveredSlotIndex = null;
            _lastTooltipSummary = null;
            e.Handled = true;
            return;
        }

        if (_secondaryLegendRect.Width > 0 && _secondaryLegendRect.Contains(pos))
        {
            IsSecondarySeriesVisible = !IsSecondarySeriesVisible;
            _hoveredSlotIndex = null;
            _lastTooltipSummary = null;
            e.Handled = true;
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width < 120 || height < 90)
        {
            return;
        }

        var cardRect = new Rect(0.5, 0.5, width - 1.0, height - 1.0);
        context.DrawRectangle(CardBackgroundBrush, CardBorderPen, new RoundedRect(cardRect, 8));

        DrawHeaderAndLegend(context, width);

        var primaryAll = FilterValidPoints(SeriesPoints);
        var secondaryAll = FilterValidPoints(SecondarySeriesPoints);

        Rect plotRect = ComputePlotRect(width, height);
        _lastPlotRect = plotRect;

        if (primaryAll.Count == 0 && secondaryAll.Count == 0)
        {
            _lastTooltipSummary = null;
            _lastTooltipRect = default;
            DrawEmptyState(context, plotRect, string.IsNullOrWhiteSpace(EmptyStateMessage)
                ? "No longitudinal measurements recorded yet."
                : EmptyStateMessage);
            return;
        }

        var primary = IsPrimarySeriesVisible ? primaryAll : new List<ChartPointItem>();
        var secondary = IsSecondarySeriesVisible ? secondaryAll : new List<ChartPointItem>();

        if (primary.Count == 0 && secondary.Count == 0)
        {
            _lastTooltipSummary = null;
            _lastTooltipRect = default;
            DrawEmptyState(context, plotRect, "Series hidden — click a legend pill above to show measurements.");
            return;
        }

        context.FillRectangle(PlotBackgroundBrush, plotRect);

        var (yMin, yMax, tickStep, tickCount) = ComputeNiceYAxisScale(primary, secondary);

        for (int i = 0; i <= tickCount; i++)
        {
            double val = Math.Round(yMax - (i * tickStep), 4);
            double y = ValueToY(val, plotRect, yMin, yMax);
            context.DrawLine(GridPen, new Point(plotRect.X, y), new Point(plotRect.Right, y));

            string tickLabel = FormatTickValue(val);
            var ft = CreateText(tickLabel, 10.5, MonoTypeface, MutedInkBrush);
            double labelX = Math.Max(6.0, plotRect.X - ft.Width - 6.0);
            double labelY = Math.Clamp(y - (ft.Height / 2.0), plotRect.Y - 6.0, plotRect.Bottom - ft.Height + 6.0);
            context.DrawText(ft, new Point(labelX, labelY));
        }

        int slotCount = Math.Max(primaryAll.Count, secondaryAll.Count);
        int? hoveredTooltipSlot = null;
        if (_hoveredSlotIndex is int hs && hs >= 0 && hs < slotCount && (hs < primary.Count || hs < secondary.Count))
        {
            hoveredTooltipSlot = hs;
        }

        using (context.PushClip(plotRect))
        {
            DrawNormalBand(context, plotRect, yMin, yMax);

            DrawThresholdLine(context, plotRect, yMin, yMax, PanicHigh);
            DrawThresholdLine(context, plotRect, yMin, yMax, PanicLow);
            DrawThresholdLine(context, plotRect, yMin, yMax, CriticalThresholdLine);

            var occupiedLabelRects = new List<Rect>();
            var referenceLineYs = CollectReferenceLineYs(plotRect, yMin, yMax);

            if (secondary.Count > 0)
            {
                DrawSeries(
                    context,
                    plotRect,
                    yMin,
                    yMax,
                    secondary,
                    slotCount,
                    SecondarySeriesBrush,
                    SecondaryLinePen,
                    labelAbove: false,
                    suppressedLabelSlotIndex: hoveredTooltipSlot,
                    referenceLineYs,
                    occupiedLabelRects);
            }

            if (primary.Count > 0)
            {
                DrawSeries(
                    context,
                    plotRect,
                    yMin,
                    yMax,
                    primary,
                    slotCount,
                    PrimarySeriesBrush,
                    PrimaryLinePen,
                    labelAbove: true,
                    suppressedLabelSlotIndex: hoveredTooltipSlot,
                    referenceLineYs,
                    occupiedLabelRects);
            }

            DrawThresholdPill(context, plotRect, yMin, yMax, PanicHigh, $"Panic Hi {FormatTickValue(PanicHigh ?? 0)}", occupiedLabelRects);
            DrawThresholdPill(context, plotRect, yMin, yMax, PanicLow, $"Panic Lo {FormatTickValue(PanicLow ?? 0)}", occupiedLabelRects);
            DrawThresholdPill(context, plotRect, yMin, yMax, CriticalThresholdLine, CriticalThresholdLabel, occupiedLabelRects);

            if (hoveredTooltipSlot is int hoveredSlot)
            {
                DrawHoverCrosshairAndTooltip(context, plotRect, yMin, yMax, primary, secondary, slotCount, hoveredSlot, occupiedLabelRects);
            }
            else
            {
                _lastTooltipSummary = null;
                _lastTooltipRect = default;
            }
        }

        context.DrawRectangle(null, GridPen, new RoundedRect(plotRect, 4));
        DrawXAxisLabels(context, plotRect, primaryAll.Count >= secondaryAll.Count ? primaryAll : secondaryAll);
    }

    private static Rect ComputePlotRect(double width, double height)
    {
        const double leftMargin = 54.0;
        const double rightMargin = 18.0;
        const double topMargin = 42.0;
        const double bottomMargin = 32.0;

        double plotW = Math.Max(40.0, width - leftMargin - rightMargin);
        double plotH = Math.Max(40.0, height - topMargin - bottomMargin);
        return new Rect(leftMargin, topMargin, plotW, plotH);
    }

    private void EnsureLegendRectsComputed()
    {
        if (Bounds.Width < 120)
        {
            _primaryLegendRect = default;
            _secondaryLegendRect = default;
            return;
        }

        ComputeLegendLayout(Bounds.Width, out _, out _primaryLegendRect, out _secondaryLegendRect);
    }

    private void ComputeLegendLayout(
        double totalWidth,
        out double titleEndCursorX,
        out Rect primaryRect,
        out Rect secondaryRect)
    {
        primaryRect = default;
        secondaryRect = default;

        string titleText = string.IsNullOrWhiteSpace(Title) ? "Clinical Trend" : Title;
        var titleFt = CreateText(titleText, 13.0, HeadingTypeface, InkBrush);
        double cursorX = 14.0 + titleFt.Width + 8.0;

        if (!string.IsNullOrWhiteSpace(UnitLabel))
        {
            var unitFt = CreateText($"({UnitLabel})", 11.5, BodyTypeface, MutedInkBrush);
            cursorX += unitFt.Width + 8.0;
        }

        titleEndCursorX = cursorX;

        bool hasPrimary = SeriesPoints is { Count: > 0 };
        bool hasSecondary = SecondarySeriesPoints is { Count: > 0 };
        if (!hasPrimary && !hasSecondary)
        {
            return;
        }

        var items = new List<(int Index, string Label)>(2);
        if (hasPrimary)
        {
            items.Add((0, PrimarySeriesName));
        }
        if (hasSecondary)
        {
            items.Add((1, SecondarySeriesName));
        }

        double rightCursor = totalWidth - 12.0;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var (idx, label) = items[i];
            var ft = CreateText(label, 10.5, BodyTypeface, MutedInkBrush);
            double pillW = 8.0 + 8.0 + 5.0 + ft.Width + 8.0;
            double pillH = 22.0;
            double pillX = rightCursor - pillW;
            if (pillX <= cursorX + 4.0)
            {
                break;
            }

            var rect = new Rect(pillX, 9.0, pillW, pillH);
            if (idx == 0)
            {
                primaryRect = rect;
            }
            else
            {
                secondaryRect = rect;
            }

            rightCursor = pillX - 8.0;
        }
    }

    private void DrawHeaderAndLegend(DrawingContext context, double totalWidth)
    {
        string titleText = string.IsNullOrWhiteSpace(Title) ? "Clinical Trend" : Title;
        var titleFt = CreateText(titleText, 13.0, HeadingTypeface, InkBrush);
        context.DrawText(titleFt, new Point(14.0, 11.0));

        double cursorX = 14.0 + titleFt.Width + 8.0;
        if (!string.IsNullOrWhiteSpace(UnitLabel))
        {
            var unitFt = CreateText($"({UnitLabel})", 11.5, BodyTypeface, MutedInkBrush);
            if (cursorX + unitFt.Width < totalWidth - 16.0)
            {
                context.DrawText(unitFt, new Point(cursorX, 12.5));
            }
        }

        ComputeLegendLayout(totalWidth, out _, out Rect primaryRect, out Rect secondaryRect);
        _primaryLegendRect = primaryRect;
        _secondaryLegendRect = secondaryRect;

        if (primaryRect.Width > 0)
        {
            DrawLegendPill(
                context,
                primaryRect,
                PrimarySeriesBrush,
                PrimarySeriesName,
                IsPrimarySeriesVisible,
                _hoveredLegendIndex == 0);
        }

        if (secondaryRect.Width > 0)
        {
            DrawLegendPill(
                context,
                secondaryRect,
                SecondarySeriesBrush,
                SecondarySeriesName,
                IsSecondarySeriesVisible,
                _hoveredLegendIndex == 1);
        }
    }

    private static void DrawLegendPill(
        DrawingContext context,
        Rect pillRect,
        IBrush seriesBrush,
        string label,
        bool isVisible,
        bool isHovered)
    {
        IBrush bg = isVisible ? LegendActiveBgBrush : LegendHiddenBgBrush;
        IPen borderPen = isHovered ? LegendHoverPen : LegendBorderPen;
        context.DrawRectangle(bg, borderPen, new RoundedRect(pillRect, 11.0));

        double dotCenterX = pillRect.X + 11.0;
        double dotCenterY = pillRect.Center.Y;
        if (isVisible)
        {
            context.DrawEllipse(seriesBrush, null, new Point(dotCenterX, dotCenterY), 4.0, 4.0);
        }
        else
        {
            context.DrawEllipse(null, new Pen(DimmedInkBrush, 1.4), new Point(dotCenterX, dotCenterY), 3.6, 3.6);
        }

        var textBrush = isVisible ? InkBrush : DimmedInkBrush;
        var ft = CreateText(label, 10.5, isVisible ? HeadingTypeface : BodyTypeface, textBrush);
        double textY = pillRect.Y + ((pillRect.Height - ft.Height) / 2.0);
        context.DrawText(ft, new Point(pillRect.X + 19.0, textY));
    }

    private static void DrawEmptyState(DrawingContext context, Rect plotRect, string msg)
    {
        context.DrawRectangle(PlotBackgroundBrush, EmptyDashedPen, new RoundedRect(plotRect, 6));
        var ft = CreateText(msg, 12.0, BodyTypeface, MutedInkBrush);
        double x = Math.Max(plotRect.X + 8.0, plotRect.X + ((plotRect.Width - ft.Width) / 2.0));
        double y = Math.Max(plotRect.Y + 8.0, plotRect.Y + ((plotRect.Height - ft.Height) / 2.0));
        context.DrawText(ft, new Point(x, y));
    }

    private void DrawNormalBand(DrawingContext context, Rect plotRect, double yMin, double yMax)
    {
        if (!NormalLow.HasValue || !NormalHigh.HasValue)
        {
            return;
        }

        double low = NormalLow.Value;
        double high = NormalHigh.Value;
        if (!double.IsFinite(low) || !double.IsFinite(high) || high <= low)
        {
            return;
        }

        double yTop = ValueToY(high, plotRect, yMin, yMax);
        double yBot = ValueToY(low, plotRect, yMin, yMax);
        var bandRect = new Rect(plotRect.X, Math.Min(yTop, yBot), plotRect.Width, Math.Max(1.0, Math.Abs(yBot - yTop)));
        context.FillRectangle(NormalBandFillBrush, bandRect);
        context.DrawLine(NormalRangePen, new Point(plotRect.X, yTop), new Point(plotRect.Right, yTop));
        context.DrawLine(NormalRangePen, new Point(plotRect.X, yBot), new Point(plotRect.Right, yBot));
    }

    private static void DrawThresholdLine(
        DrawingContext context,
        Rect plotRect,
        double yMin,
        double yMax,
        double? threshold)
    {
        if (!threshold.HasValue || !double.IsFinite(threshold.Value))
        {
            return;
        }

        double y = ValueToY(threshold.Value, plotRect, yMin, yMax);
        if (y < plotRect.Y - 2.0 || y > plotRect.Bottom + 2.0)
        {
            return;
        }

        context.DrawLine(PanicLinePen, new Point(plotRect.X, y), new Point(plotRect.Right, y));
    }

    private static void DrawThresholdPill(
        DrawingContext context,
        Rect plotRect,
        double yMin,
        double yMax,
        double? threshold,
        string label,
        List<Rect> occupiedLabelRects)
    {
        if (!threshold.HasValue || !double.IsFinite(threshold.Value) || string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        double y = ValueToY(threshold.Value, plotRect, yMin, yMax);
        if (y < plotRect.Y - 2.0 || y > plotRect.Bottom + 2.0)
        {
            return;
        }

        var ft = CreateText(label, 10.0, HeadingTypeface, PanicRedBrush);
        double maxTextX = Math.Max(plotRect.X + 8.0, plotRect.Right - ft.Width - 8.0);
        double leftInsetX = Math.Clamp(plotRect.X + 8.0, plotRect.X + 6.0, maxTextX);
        double stepRight1 = Math.Clamp(plotRect.X + 82.0, plotRect.X + 6.0, maxTextX);
        double stepRight2 = Math.Clamp(plotRect.X + 168.0, plotRect.X + 6.0, maxTextX);
        double rightInsetX = Math.Clamp(plotRect.Right - ft.Width - 8.0, plotRect.X + 6.0, maxTextX);

        double aboveY = y - ft.Height - 3.0;
        double belowY = y + 3.0;

        double[] xCandidates = { leftInsetX, stepRight1, stepRight2, rightInsetX };
        double[] yCandidates = aboveY >= plotRect.Y + 2.0
            ? new[] { aboveY, belowY }
            : new[] { belowY, aboveY };

        double chosenX = leftInsetX;
        double chosenY = Math.Clamp(yCandidates[0], plotRect.Y + 2.0, Math.Max(plotRect.Y + 2.0, plotRect.Bottom - ft.Height - 2.0));
        Rect chosenRect = new Rect(chosenX - 4.0, chosenY - 1.5, ft.Width + 8.0, ft.Height + 3.0);
        bool foundClean = false;

        foreach (double cy in yCandidates)
        {
            if (cy < plotRect.Y + 2.0 || cy + ft.Height > plotRect.Bottom - 2.0)
            {
                continue;
            }

            foreach (double cx in xCandidates)
            {
                var candidate = new Rect(cx - 4.0, cy - 1.5, ft.Width + 8.0, ft.Height + 3.0);
                if (!IntersectsAny(candidate, occupiedLabelRects))
                {
                    chosenX = cx;
                    chosenY = cy;
                    chosenRect = candidate;
                    foundClean = true;
                    break;
                }
            }

            if (foundClean)
            {
                break;
            }
        }

        context.DrawRectangle(ThresholdPillBgBrush, ThresholdPillBorderPen, new RoundedRect(chosenRect, 4.0));
        context.DrawText(ft, new Point(chosenX, chosenY));
        occupiedLabelRects.Add(chosenRect);
    }

    private List<double> CollectReferenceLineYs(Rect plotRect, double yMin, double yMax)
    {
        var ys = new List<double>(5);
        void TryAdd(double? value)
        {
            if (value is double v && double.IsFinite(v))
            {
                ys.Add(ValueToY(v, plotRect, yMin, yMax));
            }
        }

        TryAdd(NormalLow);
        TryAdd(NormalHigh);
        TryAdd(PanicLow);
        TryAdd(PanicHigh);
        TryAdd(CriticalThresholdLine);
        return ys;
    }

    private static void DrawSeries(
        DrawingContext context,
        Rect plotRect,
        double yMin,
        double yMax,
        IReadOnlyList<ChartPointItem> points,
        int slotCount,
        IBrush defaultBrush,
        IPen linePen,
        bool labelAbove,
        int? suppressedLabelSlotIndex,
        IReadOnlyList<double> referenceLineYs,
        List<Rect> occupiedLabelRects)
    {
        var coords = new Point[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            double x = IndexToX(i, Math.Max(slotCount, points.Count), plotRect);
            double y = ValueToY(points[i].Value, plotRect, yMin, yMax);
            coords[i] = new Point(x, y);
        }

        for (int i = 1; i < coords.Length; i++)
        {
            context.DrawLine(linePen, coords[i - 1], coords[i]);
        }

        bool showPointValues = points.Count <= 8;
        for (int i = 0; i < points.Count; i++)
        {
            var pt = points[i];
            var coord = coords[i];
            IBrush dotBrush = pt.IsCriticalPanic
                ? PanicRedBrush
                : pt.IsOutOfNormalRange
                    ? WarningAmberBrush
                    : defaultBrush;

            if (pt.IsCriticalPanic)
            {
                context.DrawEllipse(PanicHaloBrush, null, coord, 8.5, 8.5);
                context.DrawEllipse(dotBrush, DotRingPen, coord, 5.5, 5.5);
                occupiedLabelRects.Add(new Rect(coord.X - 9.0, coord.Y - 9.0, 18.0, 18.0));
            }
            else
            {
                context.DrawEllipse(dotBrush, DotRingPen, coord, 4.5, 4.5);
                occupiedLabelRects.Add(new Rect(coord.X - 6.0, coord.Y - 6.0, 12.0, 12.0));
            }

            if ((showPointValues || pt.IsCriticalPanic) && i != suppressedLabelSlotIndex)
            {
                string valueText = FormatTickValue(pt.Value);
                var ft = CreateText(valueText, 10.0, MonoTypeface, pt.IsCriticalPanic ? PanicRedBrush : InkBrush);
                var (labelX, labelY) = ResolvePointLabelPlacement(
                    coord,
                    ft.Width,
                    ft.Height,
                    plotRect,
                    labelAbove,
                    referenceLineYs,
                    occupiedLabelRects);

                var backdrop = new Rect(labelX - 2.0, labelY - 1.0, ft.Width + 4.0, ft.Height + 2.0);
                context.DrawRectangle(LabelBackdropBrush, null, new RoundedRect(backdrop, 3.0));
                context.DrawText(ft, new Point(labelX, labelY));
                occupiedLabelRects.Add(backdrop);
            }
        }
    }

    private void DrawHoverCrosshairAndTooltip(
        DrawingContext context,
        Rect plotRect,
        double yMin,
        double yMax,
        IReadOnlyList<ChartPointItem> primary,
        IReadOnlyList<ChartPointItem> secondary,
        int slotCount,
        int hoveredSlot,
        IReadOnlyList<Rect> occupiedLabelRects)
    {
        double crosshairX = IndexToX(hoveredSlot, slotCount, plotRect);
        context.DrawLine(CrosshairPen, new Point(crosshairX, plotRect.Top), new Point(crosshairX, plotRect.Bottom));

        ChartPointItem? pItem = hoveredSlot < primary.Count ? primary[hoveredSlot] : null;
        ChartPointItem? sItem = hoveredSlot < secondary.Count ? secondary[hoveredSlot] : null;
        if (pItem is null && sItem is null)
        {
            _lastTooltipSummary = null;
            _lastTooltipRect = default;
            return;
        }

        const double HoverHaloRadius = 9.5;
        const double TooltipHaloClearance = 18.0;
        if (pItem is not null)
        {
            double py = ValueToY(pItem.Value, plotRect, yMin, yMax);
            context.DrawEllipse(HoverHaloBrush, null, new Point(crosshairX, py), HoverHaloRadius, HoverHaloRadius);
            context.DrawEllipse(null, HoverRingPen, new Point(crosshairX, py), 6.2, 6.2);
        }

        if (sItem is not null)
        {
            double sy = ValueToY(sItem.Value, plotRect, yMin, yMax);
            context.DrawEllipse(HoverHaloBrush, null, new Point(crosshairX, sy), HoverHaloRadius, HoverHaloRadius);
            context.DrawEllipse(null, HoverRingPen, new Point(crosshairX, sy), 6.2, 6.2);
        }

        string header = pItem?.FullTimestampText
                        ?? sItem?.FullTimestampText
                        ?? pItem?.Label
                        ?? sItem?.Label
                        ?? string.Empty;
        string unitSuffix = string.IsNullOrWhiteSpace(UnitLabel) ? string.Empty : $" {UnitLabel}";

        var lines = new List<(string Text, IBrush Brush, IBrush? DotBrush, Typeface Face)>(4);
        lines.Add((header, TooltipHeaderBrush, null, HeadingTypeface));

        if (pItem is not null)
        {
            string status = BuildPointStatusTag(pItem);
            IBrush textBrush = pItem.IsCriticalPanic
                ? TooltipPanicBrush
                : pItem.IsOutOfNormalRange
                    ? TooltipAmberBrush
                    : TooltipHeaderBrush;
            lines.Add(($"{PrimarySeriesName}: {FormatTickValue(pItem.Value)}{unitSuffix}{status}", textBrush, PrimarySeriesBrush, MonoTypeface));
        }

        if (sItem is not null)
        {
            string status = BuildPointStatusTag(sItem);
            IBrush textBrush = sItem.IsCriticalPanic
                ? TooltipPanicBrush
                : sItem.IsOutOfNormalRange
                    ? TooltipAmberBrush
                    : TooltipHeaderBrush;
            lines.Add(($"{SecondarySeriesName}: {FormatTickValue(sItem.Value)}{unitSuffix}{status}", textBrush, SecondarySeriesBrush, MonoTypeface));
        }

        string? detail = pItem?.DetailText ?? sItem?.DetailText;
        if (!string.IsNullOrWhiteSpace(detail))
        {
            string clampedDetail = detail.Length > 56 ? detail[..53] + "..." : detail;
            lines.Add((clampedDetail, TooltipSubtextBrush, null, BodyTypeface));
        }

        _lastTooltipSummary = string.Join(" | ", lines.ConvertAll(l => l.Text));

        var formatted = new List<(FormattedText Ft, IBrush? DotBrush)>(lines.Count);
        double maxLineWidth = 0.0;
        double totalTextHeight = 0.0;
        const double lineGap = 3.0;

        for (int i = 0; i < lines.Count; i++)
        {
            var (txt, brush, dotBrush, face) = lines[i];
            double fontSize = i == 0 ? 11.0 : 10.5;
            var ft = CreateText(txt, fontSize, face, brush);
            double dotOffset = dotBrush is not null ? 11.0 : 0.0;
            maxLineWidth = Math.Max(maxLineWidth, ft.Width + dotOffset);
            totalTextHeight += ft.Height + (i > 0 ? lineGap : 0.0);
            formatted.Add((ft, dotBrush));
        }

        const double padX = 10.0;
        const double padY = 7.0;
        double boxW = Math.Min(plotRect.Width - 8.0, maxLineWidth + (padX * 2.0));
        double boxH = Math.Min(plotRect.Height - 8.0, totalTextHeight + (padY * 2.0));

        double horizGap = HoverHaloRadius + TooltipHaloClearance;
        bool inRightHalf = crosshairX >= plotRect.X + (plotRect.Width * 0.50);
        bool preferLeft = inRightHalf || (crosshairX + horizGap + boxW > plotRect.Right - 4.0);
        double boxX = preferLeft
            ? crosshairX - horizGap - boxW
            : crosshairX + horizGap;
        boxX = Math.Clamp(boxX, plotRect.X + 4.0, Math.Max(plotRect.X + 4.0, plotRect.Right - boxW - 4.0));

        double anchorY = pItem is not null
            ? ValueToY(pItem.Value, plotRect, yMin, yMax)
            : ValueToY(sItem!.Value, plotRect, yMin, yMax);

        bool inUpperHalf = anchorY <= plotRect.Y + (plotRect.Height * 0.50);
        double belowY = Math.Clamp(anchorY + 14.0, plotRect.Y + 4.0, Math.Max(plotRect.Y + 4.0, plotRect.Bottom - boxH - 4.0));
        double centeredY = Math.Clamp(anchorY - (boxH / 2.0), plotRect.Y + 4.0, Math.Max(plotRect.Y + 4.0, plotRect.Bottom - boxH - 4.0));
        double aboveY = Math.Clamp(anchorY - boxH - 14.0, plotRect.Y + 4.0, Math.Max(plotRect.Y + 4.0, plotRect.Bottom - boxH - 4.0));
        double bottomClearY = Math.Max(plotRect.Y + 4.0, plotRect.Bottom - boxH - 4.0);

        double[] candidateYs = inUpperHalf
            ? new[] { belowY, bottomClearY, centeredY, aboveY }
            : new[] { aboveY, centeredY, belowY, plotRect.Y + 4.0 };

        double boxY = candidateYs[0];
        var tooltipRect = new Rect(boxX, boxY, boxW, boxH);
        foreach (double cy in candidateYs)
        {
            var probe = new Rect(boxX, cy, boxW, boxH);
            if (!IntersectsAny(probe, occupiedLabelRects))
            {
                boxY = cy;
                tooltipRect = probe;
                break;
            }
        }

        _lastTooltipRect = tooltipRect;
        context.DrawRectangle(TooltipBgBrush, TooltipBorderPen, new RoundedRect(tooltipRect, 6.0));

        double yCursor = boxY + padY;
        for (int i = 0; i < formatted.Count; i++)
        {
            var (ft, dotBrush) = formatted[i];
            if (yCursor + ft.Height > tooltipRect.Bottom - 2.0)
            {
                break;
            }

            double xCursor = boxX + padX;
            if (dotBrush is not null)
            {
                context.DrawEllipse(dotBrush, null, new Point(xCursor + 3.0, yCursor + (ft.Height / 2.0)), 3.2, 3.2);
                xCursor += 11.0;
            }

            context.DrawText(ft, new Point(xCursor, yCursor));
            yCursor += ft.Height + lineGap;
        }
    }

    private static string BuildPointStatusTag(ChartPointItem pt)
    {
        if (pt.IsCriticalPanic)
        {
            return " (Panic)";
        }

        if (pt.IsOutOfNormalRange)
        {
            return !string.IsNullOrWhiteSpace(pt.AnnotationText)
                ? $" ({pt.AnnotationText})"
                : " (Out of Range)";
        }

        return string.Empty;
    }

    private static (double X, double Y) ResolvePointLabelPlacement(
        Point dot,
        double labelWidth,
        double labelHeight,
        Rect plotRect,
        bool preferAbove,
        IReadOnlyList<double> referenceLineYs,
        List<Rect> occupiedLabelRects)
    {
        double centeredX = Math.Clamp(dot.X - (labelWidth / 2.0), plotRect.X + 2.0, plotRect.Right - labelWidth - 2.0);
        double shiftedLeftX = Math.Clamp(dot.X - labelWidth - 8.0, plotRect.X + 2.0, plotRect.Right - labelWidth - 2.0);
        double shiftedRightX = Math.Clamp(dot.X + 8.0, plotRect.X + 2.0, plotRect.Right - labelWidth - 2.0);

        double above1 = dot.Y - labelHeight - 10.0;
        double below1 = dot.Y + 9.0;
        double above2 = dot.Y - labelHeight - 22.0;
        double below2 = dot.Y + 21.0;

        double[] yCandidates = preferAbove
            ? new[] { above1, below1, above2, below2 }
            : new[] { below1, above1, below2, above2 };

        double[] xCandidates = { centeredX, shiftedLeftX, shiftedRightX };

        foreach (double cy in yCandidates)
        {
            if (cy < plotRect.Y + 2.0 || cy + labelHeight > plotRect.Bottom - 2.0)
            {
                continue;
            }

            if (IntersectsReferenceLine(cy, labelHeight, referenceLineYs))
            {
                continue;
            }

            foreach (double cx in xCandidates)
            {
                var candidate = new Rect(cx - 2.0, cy - 1.0, labelWidth + 4.0, labelHeight + 2.0);
                if (!IntersectsAny(candidate, occupiedLabelRects))
                {
                    return (cx, cy);
                }
            }
        }

        // Fallback: pick candidate fitting plotRect with maximum clearance from reference lines and least overlap
        double bestX = centeredX;
        double bestY = Math.Clamp(yCandidates[0], plotRect.Y + 2.0, Math.Max(plotRect.Y + 2.0, plotRect.Bottom - labelHeight - 2.0));
        double bestScore = double.NegativeInfinity;

        foreach (double cy in yCandidates)
        {
            double clampedY = Math.Clamp(cy, plotRect.Y + 2.0, Math.Max(plotRect.Y + 2.0, plotRect.Bottom - labelHeight - 2.0));
            double refClearance = MinimumReferenceClearance(clampedY, labelHeight, referenceLineYs);

            foreach (double cx in xCandidates)
            {
                var candidate = new Rect(cx - 2.0, clampedY - 1.0, labelWidth + 4.0, labelHeight + 2.0);
                double overlapPenalty = IntersectsAny(candidate, occupiedLabelRects) ? 100.0 : 0.0;
                double linePenalty = IntersectsReferenceLine(clampedY, labelHeight, referenceLineYs) ? 40.0 : 0.0;
                double score = refClearance - overlapPenalty - linePenalty;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestX = cx;
                    bestY = clampedY;
                }
            }
        }

        return (bestX, bestY);
    }

    private static bool IntersectsAny(Rect candidate, IReadOnlyList<Rect> occupied)
    {
        var padded = candidate.Inflate(1.5);
        for (int i = 0; i < occupied.Count; i++)
        {
            if (padded.Intersects(occupied[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IntersectsReferenceLine(double labelY, double labelHeight, IReadOnlyList<double> referenceLineYs)
    {
        double top = labelY - 1.5;
        double bottom = labelY + labelHeight + 1.5;
        for (int i = 0; i < referenceLineYs.Count; i++)
        {
            double lineY = referenceLineYs[i];
            if (lineY >= top && lineY <= bottom)
            {
                return true;
            }
        }

        return false;
    }

    private static double MinimumReferenceClearance(double labelY, double labelHeight, IReadOnlyList<double> referenceLineYs)
    {
        double center = labelY + (labelHeight / 2.0);
        double minDist = 100.0;
        for (int i = 0; i < referenceLineYs.Count; i++)
        {
            double dist = Math.Abs(referenceLineYs[i] - center);
            if (dist < minDist)
            {
                minDist = dist;
            }
        }

        return minDist;
    }

    private static void DrawXAxisLabels(DrawingContext context, Rect plotRect, IReadOnlyList<ChartPointItem> points)
    {
        if (points.Count == 0)
        {
            return;
        }

        string FormatXLabel(string? raw)
        {
            string s = raw ?? string.Empty;
            return s.Length > 16 ? s[..15] + "…" : s;
        }

        var lastFt = CreateText(FormatXLabel(points[^1].Label), 10.0, BodyTypeface, MutedInkBrush);
        double lastX = Math.Clamp(
            IndexToX(points.Count - 1, points.Count, plotRect) - (lastFt.Width / 2.0),
            plotRect.X,
            plotRect.Right - lastFt.Width);

        double lastDrawnRight = double.NegativeInfinity;
        int maxLabels = Math.Max(2, (int)(plotRect.Width / 82.0));
        int step = Math.Max(1, (int)Math.Ceiling(points.Count / (double)maxLabels));

        for (int i = 0; i < points.Count - 1; i++)
        {
            if (i % step != 0)
            {
                continue;
            }

            string text = FormatXLabel(points[i].Label);
            var ft = CreateText(text, 10.0, BodyTypeface, MutedInkBrush);
            double x = IndexToX(i, points.Count, plotRect) - (ft.Width / 2.0);
            x = Math.Clamp(x, plotRect.X, plotRect.Right - ft.Width);

            if (x < lastDrawnRight + 8.0)
            {
                continue;
            }

            if (x + ft.Width + 8.0 > lastX)
            {
                continue;
            }

            context.DrawText(ft, new Point(x, plotRect.Bottom + 6.0));
            lastDrawnRight = x + ft.Width;
        }

        if (points.Count == 1 || lastX >= lastDrawnRight + 6.0)
        {
            context.DrawText(lastFt, new Point(lastX, plotRect.Bottom + 6.0));
        }
    }

    private static readonly double[] ClinicalAxisSteps =
        { 0.1, 0.2, 0.25, 0.5, 1.0, 2.0, 2.5, 5.0, 10.0, 20.0, 25.0, 50.0, 100.0, 200.0, 250.0, 500.0 };

    private (double Min, double Max, double Step, int TickCount) ComputeNiceYAxisScale(
        IReadOnlyList<ChartPointItem> primary,
        IReadOnlyList<ChartPointItem> secondary)
    {
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;

        void Expand(double? candidate)
        {
            if (candidate.HasValue && double.IsFinite(candidate.Value))
            {
                min = Math.Min(min, candidate.Value);
                max = Math.Max(max, candidate.Value);
            }
        }

        foreach (var p in primary) Expand(p.Value);
        foreach (var p in secondary) Expand(p.Value);
        Expand(NormalLow);
        Expand(NormalHigh);
        Expand(PanicLow);
        Expand(PanicHigh);
        Expand(CriticalThresholdLine);

        if (!double.IsFinite(min) || !double.IsFinite(max))
        {
            return (0.0, 100.0, 25.0, 4);
        }

        double span = max - min;
        if (span < 1e-6)
        {
            span = Math.Max(2.0, Math.Abs(min) * 0.24);
        }

        double targetMin = min - (span * 0.14);
        double targetMax = max + (span * 0.18);

        foreach (double step in ClinicalAxisSteps)
        {
            double snapMin = Math.Floor(targetMin / step) * step;
            double snapMax = Math.Ceiling(targetMax / step) * step;
            int intervals = (int)Math.Round((snapMax - snapMin) / step);
            if (intervals is >= 4 and <= 6)
            {
                return (snapMin, snapMax, step, intervals);
            }
        }

        double roughStep = Math.Max(1e-3, (targetMax - targetMin) / 4.0);
        double pow10 = Math.Pow(10.0, Math.Floor(Math.Log10(roughStep)));
        double norm = roughStep / pow10;
        double niceFactor = norm <= 1.0 ? 1.0 : norm <= 2.0 ? 2.0 : norm <= 5.0 ? 5.0 : 10.0;
        double fallbackStep = niceFactor * pow10;
        double fbMin = Math.Floor(targetMin / fallbackStep) * fallbackStep;
        double fbMax = Math.Ceiling(targetMax / fallbackStep) * fallbackStep;
        int fbIntervals = Math.Max(4, (int)Math.Round((fbMax - fbMin) / fallbackStep));
        return (fbMin, fbMax, fallbackStep, fbIntervals);
    }

    private static List<ChartPointItem> FilterValidPoints(IReadOnlyList<ChartPointItem>? source)
    {
        var list = new List<ChartPointItem>();
        if (source is null)
        {
            return list;
        }

        foreach (var item in source)
        {
            if (item is not null && double.IsFinite(item.Value))
            {
                list.Add(item);
            }
        }

        return list;
    }

    private static double IndexToX(int index, int count, Rect plotRect)
    {
        if (count <= 1)
        {
            return plotRect.X + (plotRect.Width / 2.0);
        }

        double ratio = index / (double)(count - 1);
        return plotRect.X + 10.0 + (ratio * Math.Max(1.0, plotRect.Width - 20.0));
    }

    private static double ValueToY(double value, Rect plotRect, double yMin, double yMax)
    {
        double span = Math.Max(1e-6, yMax - yMin);
        double norm = (value - yMin) / span;
        return plotRect.Bottom - (norm * plotRect.Height);
    }

    private static string FormatTickValue(double val)
    {
        return Math.Abs(val) >= 100.0
            ? val.ToString("0", CultureInfo.InvariantCulture)
            : val.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static FormattedText CreateText(string text, double size, Typeface typeface, IBrush brush)
    {
        return new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            size,
            brush);
    }
}
