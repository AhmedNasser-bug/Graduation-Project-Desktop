namespace NephroEdge.Desktop.Controls;

/// <summary>
/// Immutable single chronological observation point rendered inside <see cref="ClinicalTrendChartControl"/>.
/// </summary>
public sealed record ChartPointItem(
    string Label,
    double Value,
    bool IsOutOfNormalRange = false,
    bool IsCriticalPanic = false,
    string? AnnotationText = null,
    string? FullTimestampText = null,
    string? DetailText = null
);
