using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using NephroEdge.Desktop.ViewModels;

namespace NephroEdge.Desktop.Infrastructure;

/// <summary>
/// Multi-value converter resolving owning patient full name and MRN for queued
/// <c>OrderHandshakeRecord</c> entries inside SyncMonitor without changing entity contracts.
/// </summary>
public sealed class PatientQueueLabelConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
        {
            return string.Empty;
        }

        string? patientId = values[0]?.ToString();
        if (string.IsNullOrWhiteSpace(patientId))
        {
            return string.Empty;
        }

        if (values[1] is SyncMonitorViewModel viewModel)
        {
            return viewModel.ResolvePatientNameAndMrn(patientId);
        }

        return patientId;
    }
}
