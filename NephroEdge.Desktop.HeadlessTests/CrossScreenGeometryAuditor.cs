using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using NephroEdge.Desktop.Controls;

namespace NephroEdge.Desktop.HeadlessTests;

/// <summary>
/// Automated cross-screen geometry, viewport clipping, sibling overlap, ISMP medication typography,
/// and IEC 60601-1-8 color-reservation auditor for rendered Avalonia visual trees.
/// </summary>
public static class CrossScreenGeometryAuditor
{
    private static readonly Regex LeadingZeroViolationRegex = new(
        @"(?<![\d.])\.\d+\s*(?:mg|g|mcg|mL|L|kg|units|mEq)\b",
        RegexOptions.Compiled);

    private static readonly Regex TrailingZeroViolationRegex = new(
        @"(?:\b\d+\.0+\s*(?:mg|g|mcg|mL|L|units|mEq/L|g/dL|mmol/L)\b|\b\d+\.0+(?=\s*-\s*\d+(?:\.\d+)?\s*(?:mg|g|mcg|mL|L|g/dL|mmol/L|mEq/L)\b))",
        RegexOptions.Compiled);

    private static readonly Regex AmbiguousUnitViolationRegex = new(
        @"\b\d+(?:\.\d+)?\s*(?:u|i\.?u\.?)\b(?![a-zA-Z])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LowercaseLiterViolationRegex = new(
        @"(?:\b\d+(?:\.\d+)?\s*l\b|/l\b)",
        RegexOptions.Compiled);

    private static readonly Regex StandaloneInProgressRegex = new(
        @"\bInProgress\b",
        RegexOptions.Compiled);

    private static readonly string[] ForbiddenDeveloperJargon =
    {
        "LOINC 3NF",
        "LAST-N",
        "SYNCOUTBOXQUEUE",
        "LocalDeterministic",
        "WarningAdvisory",
        "InsufficientData",
        "LatestSbpMmHg",
        "DeltaSbpFromPreBaselineMmHg",
        "HistoricalNadirMinSbpMmHg",
        "IV_Circuit",
        "AV_Fistula",
        "CONFLICT_REQUIRES_CLINICIAN_ACK",
        "PENDING_ACK",
        "SyncVersion",
        "OrderHandshake",
        "Delta Baseline · Baseline"
    };

    private static readonly (string Pattern, string ExpectedTallMan)[] TallManChecks =
    {
        ("hydroxyzine", "hydrOXYzine"),
        ("hydralazine", "hydrALAzine"),
        ("hydromorphone", "HYDROmorphone")
    };

    private static readonly HashSet<uint> ForbiddenTechnicalRedColors = new()
    {
        Color.Parse("#DC2626").ToUInt32(),
        Color.Parse("#EF4444").ToUInt32(),
        Color.Parse("#B91C1C").ToUInt32(),
        Color.Parse("#991B1B").ToUInt32(),
        Color.Parse("#FEE2E2").ToUInt32(),
        Color.Parse("#FEF2F2").ToUInt32(),
        Color.Parse("#FCA5A5").ToUInt32()
    };

    public static void AssertNotInsideScrollViewer(Control control, string controlName)
    {
        ArgumentNullException.ThrowIfNull(control);

        Visual? current = control.GetVisualParent();
        while (current is not null)
        {
            if (current is ScrollViewer)
            {
                throw new InvalidOperationException(
                    $"[IPSG.1 VIOLATION] Control '{controlName}' must stay pinned outside any ScrollViewer.");
            }

            current = current.GetVisualParent();
        }
    }

    public static IReadOnlyList<string> AuditWindowGeometry(Window window, string screenTag)
    {
        var violations = CollectWindowGeometryViolations(window, screenTag);

        if (violations.Count > 0)
        {
            throw new InvalidOperationException(
                $"[GEOMETRY AUDIT FAILED: {screenTag}] {violations.Count} issue(s):\n - " +
                string.Join("\n - ", violations.Take(15)));
        }

        return violations;
    }

    public static IReadOnlyList<string> CollectWindowGeometryViolations(Window window, string screenTag)
    {
        ArgumentNullException.ThrowIfNull(window);

        var violations = new List<string>();
        double winW = window.Bounds.Width;
        double winH = window.Bounds.Height;
        if (winW <= 0 || winH <= 0)
        {
            violations.Add($"[{screenTag}] Window has invalid bounds {winW}x{winH}.");
            return violations;
        }

        foreach (Visual visual in window.GetVisualDescendants())
        {
            if (visual is not Control control || !control.IsEffectivelyVisible || IsTemplateInternalChrome(control))
            {
                continue;
            }

            bool isTargetInteractive =
                (control is Button && control is not RepeatButton)
                || control is TextBox
                || control is ComboBox
                || control is ListBox
                || control is ClinicalTrendChartControl;

            if (isTargetInteractive)
            {
                if (control.Bounds.Width <= 0.0 || control.Bounds.Height <= 0.0)
                {
                    violations.Add(
                        $"[{screenTag}] Zero/negative bounds on {DescribeControl(control)}: {control.Bounds.Width:0.#}x{control.Bounds.Height:0.#}.");
                    continue;
                }

                Rect? rectInWin = TransformBoundsToAncestor(control, window);
                if (rectInWin is not Rect r)
                {
                    continue;
                }

                ScrollViewer? sv = FindScrollViewerAncestor(control);
                if (sv is null)
                {
                    if (r.Left < -2.0 || r.Top < -2.0 || r.Right > winW + 2.0 || r.Bottom > winH + 2.0)
                    {
                        violations.Add(
                            $"[{screenTag}] Viewport clip outside window ({winW:0.#}x{winH:0.#}) for {DescribeControl(control)}: rect=({r.Left:0.#},{r.Top:0.#},{r.Right:0.#},{r.Bottom:0.#}).");
                    }
                }
                else if (sv.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled)
                {
                    Rect? svRect = TransformBoundsToAncestor(sv, window);
                    if (svRect is Rect svr)
                    {
                        if (r.Left < svr.Left - 3.0 || r.Right > svr.Right + 3.0 || r.Right > winW + 2.0)
                        {
                            violations.Add(
                                $"[{screenTag}] Horizontal overflow inside vertical ScrollViewer for {DescribeControl(control)}: ctrl=({r.Left:0.#}..{r.Right:0.#}), sv=({svr.Left:0.#}..{svr.Right:0.#}), winW={winW:0.#}.");
                        }
                    }
                }
            }

            if (control is Panel panel)
            {
                AuditPanelSiblingOverlaps(panel, screenTag, violations);
            }
        }

        return violations;
    }

    public static void AuditIsmpTypography(Control root, string contextTag)
    {
        ArgumentNullException.ThrowIfNull(root);
        var violations = new List<string>();

        foreach (Visual visual in root.GetVisualDescendants())
        {
            if (visual is not TextBlock tb || !tb.IsEffectivelyVisible || string.IsNullOrWhiteSpace(tb.Text))
            {
                continue;
            }

            string text = tb.Text;

            if (LeadingZeroViolationRegex.IsMatch(text))
            {
                violations.Add($"Leading zero missing in '{text}' ({DescribeControl(tb)})");
            }

            if (TrailingZeroViolationRegex.IsMatch(text))
            {
                violations.Add($"Trailing zero present in '{text}' ({DescribeControl(tb)})");
            }

            if (AmbiguousUnitViolationRegex.IsMatch(text))
            {
                violations.Add($"Ambiguous 'u'/'IU' unit in '{text}' ({DescribeControl(tb)})");
            }

            if (LowercaseLiterViolationRegex.IsMatch(text))
            {
                violations.Add($"Lowercase 'l' liter symbol in '{text}' ({DescribeControl(tb)})");
            }

            foreach (var (pattern, expected) in TallManChecks)
            {
                if (text.Contains(pattern, StringComparison.OrdinalIgnoreCase) &&
                    !text.Contains(expected, StringComparison.Ordinal))
                {
                    violations.Add($"Tall Man lettering mismatch (expected '{expected}') in '{text}' ({DescribeControl(tb)})");
                }
            }
        }

        if (violations.Count > 0)
        {
            throw new InvalidOperationException(
                $"[ISMP TYPOGRAPHY AUDIT FAILED: {contextTag}]\n - " +
                string.Join("\n - ", violations.Take(15)));
        }
    }

    public static void AuditNoRedOnTechnicalSync(Control root, string contextTag)
    {
        ArgumentNullException.ThrowIfNull(root);
        var violations = new List<string>();

        foreach (Visual visual in root.GetVisualDescendants().Prepend(root))
        {
            if (visual is not Control ctrl || !ctrl.IsEffectivelyVisible)
            {
                continue;
            }

            if (ctrl is Border b)
            {
                CheckBrushNotRed(b.Background, "Background", b, violations);
                CheckBrushNotRed(b.BorderBrush, "BorderBrush", b, violations);
            }
            else if (ctrl is TemplatedControl tc)
            {
                CheckBrushNotRed(tc.Background, "Background", tc, violations);
                CheckBrushNotRed(tc.BorderBrush, "BorderBrush", tc, violations);
                CheckBrushNotRed(tc.Foreground, "Foreground", tc, violations);
            }
            else if (ctrl is TextBlock tb)
            {
                CheckBrushNotRed(tb.Foreground, "Foreground", tb, violations);
            }
        }

        if (violations.Count > 0)
        {
            throw new InvalidOperationException(
                $"[IEC 60601-1-8 RED-RESERVE VIOLATION: {contextTag}]\n - " +
                string.Join("\n - ", violations.Take(15)));
        }
    }

    public static void AuditNoDeveloperJargon(Control root, string contextTag)
    {
        var violations = CollectDeveloperJargonViolations(root, contextTag);
        if (violations.Count > 0)
        {
            throw new InvalidOperationException(
                $"[DEVELOPER JARGON AUDIT FAILED: {contextTag}] {violations.Count} leak(s):\n - " +
                string.Join("\n - ", violations.Take(20)));
        }
    }

    public static IReadOnlyList<string> CollectDeveloperJargonViolations(Control root, string contextTag)
    {
        ArgumentNullException.ThrowIfNull(root);
        var violations = new List<string>();

        foreach (Visual visual in root.GetVisualDescendants().Prepend(root))
        {
            if (visual is not TextBlock tb || !tb.IsEffectivelyVisible || string.IsNullOrWhiteSpace(tb.Text))
            {
                continue;
            }

            string text = tb.Text;

            foreach (string term in ForbiddenDeveloperJargon)
            {
                if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"[{contextTag}] Jargon '{term}' leaked in '{text}' ({DescribeControl(tb)})");
                }
            }

            if (StandaloneInProgressRegex.IsMatch(text))
            {
                violations.Add($"[{contextTag}] Raw enum 'InProgress' leaked in '{text}' ({DescribeControl(tb)})");
            }
        }

        return violations;
    }

    public static bool TryGetBoundsInWindow(Control control, Window window, out Rect rect)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(window);

        if (!control.IsEffectivelyVisible || control.Bounds.Width <= 0.0 || control.Bounds.Height <= 0.0)
        {
            rect = default;
            return false;
        }

        Rect? mapped = TransformBoundsToAncestor(control, window);
        if (mapped is not Rect r)
        {
            rect = default;
            return false;
        }

        rect = r;
        return true;
    }

    public static Point GetControlCenterInWindow(Control control, Window window)
    {
        if (!TryGetBoundsInWindow(control, window, out Rect rect))
        {
            throw new InvalidOperationException(
                $"Control {DescribeControl(control)} has no visible mapped bounds inside window.");
        }

        return rect.Center;
    }

    public static Rect AuditAboveTheFold(
        Control control,
        Window window,
        double maxTop,
        string contextTag,
        double? maxBottom = null)
    {
        if (!TryGetBoundsInWindow(control, window, out Rect rect))
        {
            throw new InvalidOperationException(
                $"[ABOVE-THE-FOLD FAILED: {contextTag}] Control {DescribeControl(control)} is not visible or has zero bounds.");
        }

        if (rect.Top > maxTop)
        {
            throw new InvalidOperationException(
                $"[ABOVE-THE-FOLD FAILED: {contextTag}] {DescribeControl(control)} Top={rect.Top:0.#} exceeds maxTop={maxTop:0.#} (window={window.Bounds.Width:0.#}x{window.Bounds.Height:0.#}).");
        }

        if (maxBottom.HasValue && rect.Bottom > maxBottom.Value)
        {
            throw new InvalidOperationException(
                $"[ABOVE-THE-FOLD FAILED: {contextTag}] {DescribeControl(control)} Bottom={rect.Bottom:0.#} exceeds maxBottom={maxBottom.Value:0.#} (window={window.Bounds.Width:0.#}x{window.Bounds.Height:0.#}).");
        }

        return rect;
    }

    public static void AuditVerticalOrder(Control upper, Control lower, Window window, string contextTag)
    {
        if (!TryGetBoundsInWindow(upper, window, out Rect upperRect))
        {
            throw new InvalidOperationException(
                $"[VERTICAL ORDER FAILED: {contextTag}] Upper control {DescribeControl(upper)} is not visible.");
        }

        if (!TryGetBoundsInWindow(lower, window, out Rect lowerRect))
        {
            throw new InvalidOperationException(
                $"[VERTICAL ORDER FAILED: {contextTag}] Lower control {DescribeControl(lower)} is not visible.");
        }

        if (upperRect.Top >= lowerRect.Top)
        {
            throw new InvalidOperationException(
                $"[VERTICAL ORDER FAILED: {contextTag}] Expected {DescribeControl(upper)} (Top={upperRect.Top:0.#}) above {DescribeControl(lower)} (Top={lowerRect.Top:0.#}).");
        }
    }

    private static bool IsTemplateInternalChrome(Control control)
    {
        if (control is Popup)
        {
            return true;
        }

        if (control is RepeatButton && control.TemplatedParent is Track or ScrollBar)
        {
            return true;
        }

        return false;
    }

    private static void AuditPanelSiblingOverlaps(Panel panel, string screenTag, List<string> violations)
    {
        if (panel is not (StackPanel or WrapPanel or DockPanel or Grid))
        {
            return;
        }

        var visibleChildren = panel.Children
            .Where(c => c.IsEffectivelyVisible
                && !IsTemplateInternalChrome(c)
                && c.Bounds.Width > 0.0
                && c.Bounds.Height > 0.0)
            .ToList();

        for (int i = 0; i < visibleChildren.Count; i++)
        {
            Control a = visibleChildren[i];
            for (int j = i + 1; j < visibleChildren.Count; j++)
            {
                Control b = visibleChildren[j];

                if (panel is Grid && AreGridCellsPotentiallyShared(a, b))
                {
                    continue;
                }

                Rect intersection = a.Bounds.Intersect(b.Bounds);
                if (intersection.Width > 2.0 && intersection.Height > 2.0)
                {
                    violations.Add(
                        $"[{screenTag}] Sibling overlap ({intersection.Width:0.#}x{intersection.Height:0.#}) inside {panel.GetType().Name} ({panel.Name ?? "unnamed"}): {DescribeControl(a)} vs {DescribeControl(b)}.");
                }
            }
        }
    }

    private static bool AreGridCellsPotentiallyShared(Control a, Control b)
    {
        int colA = Grid.GetColumn(a);
        int colSpanA = Math.Max(1, Grid.GetColumnSpan(a));
        int colB = Grid.GetColumn(b);
        int colSpanB = Math.Max(1, Grid.GetColumnSpan(b));

        int rowA = Grid.GetRow(a);
        int rowSpanA = Math.Max(1, Grid.GetRowSpan(a));
        int rowB = Grid.GetRow(b);
        int rowSpanB = Math.Max(1, Grid.GetRowSpan(b));

        bool colsOverlap = colA < colB + colSpanB && colB < colA + colSpanA;
        bool rowsOverlap = rowA < rowB + rowSpanB && rowB < rowA + rowSpanA;
        return colsOverlap && rowsOverlap;
    }

    private static void CheckBrushNotRed(IBrush? brush, string slot, Control owner, List<string> violations)
    {
        if (brush is ISolidColorBrush solid && ForbiddenTechnicalRedColors.Contains(solid.Color.ToUInt32()))
        {
            violations.Add(
                $"Forbidden Red color #{solid.Color.ToUInt32():X8} on {slot} of {DescribeControl(owner)}");
        }
    }

    private static ScrollViewer? FindScrollViewerAncestor(Visual control)
    {
        Visual? current = control.GetVisualParent();
        while (current is not null)
        {
            if (current is ScrollViewer sv)
            {
                return sv;
            }

            current = current.GetVisualParent();
        }

        return null;
    }

    private static Rect? TransformBoundsToAncestor(Visual control, Visual ancestor)
    {
        Matrix? transform = control.TransformToVisual(ancestor);
        if (transform is not Matrix m)
        {
            return null;
        }

        var bounds = new Rect(0, 0, control.Bounds.Width, control.Bounds.Height);
        Point topLeft = bounds.TopLeft.Transform(m);
        Point bottomRight = bounds.BottomRight.Transform(m);
        return new Rect(topLeft, bottomRight);
    }

    private static string DescribeControl(Control control)
    {
        string typeName = control.GetType().Name;
        string namePart = string.IsNullOrWhiteSpace(control.Name) ? string.Empty : $"#{control.Name}";
        if (control is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text))
        {
            string snippet = tb.Text.Length > 28 ? tb.Text[..28] + "..." : tb.Text;
            return $"{typeName}{namePart}(\"{snippet}\")";
        }
        if (control is Button btn && btn.Content is string s)
        {
            return $"{typeName}{namePart}(\"{s}\")";
        }
        return $"{typeName}{namePart}";
    }
}
