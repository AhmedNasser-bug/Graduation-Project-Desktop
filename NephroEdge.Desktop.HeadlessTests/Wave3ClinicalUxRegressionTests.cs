using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using NephroEdge.Desktop.Infrastructure;
using NephroEdge.Desktop.ViewModels;
using NephroEdge.Desktop.Views;
using Persistence_Unit.Entities;
using Services_Unit.Core.Contracts;
using Services_Unit.Core.Interfaces;

namespace NephroEdge.Desktop.HeadlessTests;

/// <summary>
/// Wave 3 final clinical micro-polish regression checks:
/// SyncMonitor patient header & clean confirmation headline, SafetyRx concise bath & ADE watermarks,
/// normalized critical-lab status feedback, and rounded 4px ShiftGrid edge bar inset.
/// </summary>
public static class Wave3ClinicalUxRegressionTests
{
    public static void RunPreViewportWave3Checks(
        MainWindow window,
        MainWindowViewModel mainVm,
        IServiceScopeFactory scopeFactory,
        string patientAlphaId,
        string patientBetaId,
        string patientNomId,
        List<string> defectAccumulator)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(mainVm);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(defectAccumulator);

        var shiftGridView = FindRequiredDescendant<ShiftGridView>(window);
        var safetyRxView = FindRequiredDescendant<SafetyRxWorkbenchView>(window);
        var syncMonitorView = FindRequiredDescendant<SyncMonitorView>(window);

        void CaptureStep(string stepTag, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                defectAccumulator.Add($"[{stepTag}] {ex.Message}");
                Console.WriteLine($"  -> DEFECT [{stepTag}]: {ex.Message}");
            }
        }

        // -----------------------------------------------------------------
        // ITERATION 39: SyncMonitor Patient-Named Queue Cards & Clean Confirm Headline
        // -----------------------------------------------------------------
        Console.WriteLine("[ITER 39/41] SyncMonitor Queue Patient Name·MRN & Clean Order Confirmation Headline...");
        CaptureStep("ITER39:SyncMonitorWave3", () =>
        {
            mainVm.BannerVm.SelectPatientById(patientBetaId, notifyListeners: true);
            mainVm.SelectWorkspaceTab(WorkspaceTab.SyncMonitor);
            PumpUi(window);

            var queueBox = FindRequired<ListBox>(syncMonitorView, "ConflictQueueListBox");
            OrderHandshakeRecord? pendingBetaItem = mainVm.SyncMonitorVm.ConflictQueueItems
                .FirstOrDefault(h => h.PatientId == patientBetaId && h.Status == "PENDING_ACK");
            Assert(pendingBetaItem is not null, "Expected PENDING_ACK directive for PatientBeta in ConflictQueueItems.");

            ListBoxItem? betaContainer = queueBox.GetVisualDescendants()
                .OfType<ListBoxItem>()
                .FirstOrDefault(lbi => lbi.DataContext is OrderHandshakeRecord r && r.HandshakeId == pendingBetaItem!.HandshakeId);
            Assert(betaContainer is not null, "Expected rendered ListBoxItem for PatientBeta handshake.");

            bool hasPatientNameAndMrn = betaContainer!.GetVisualDescendants()
                .OfType<TextBlock>()
                .Any(tb => tb.Text is not null &&
                           tb.Text.Contains("Karim Nabil Farouk", StringComparison.Ordinal) &&
                           tb.Text.Contains("MRN-UX-BETA", StringComparison.Ordinal));
            Assert(hasPatientNameAndMrn,
                "SyncMonitor ConflictQueueListBox card must directly display owning 'Karim Nabil Farouk · MRN-UX-BETA'.");

            string ephemeralHandshakeId;
            using (var scope = scopeFactory.CreateScope())
            {
                var handshakeService = scope.ServiceProvider.GetRequiredService<IOrderHandshakeService>();
                var created = handshakeService.QueueDirective(new ClosedLoopDirectiveRequest(
                    PatientId: patientNomId,
                    SessionId: null,
                    PrescriptionId: null,
                    DirectiveType: "DRY_WEIGHT_UPDATE",
                    DirectiveSummary: "Wave 3 confirmation headline check",
                    IsmpFormattedDoseText: "74.5 kg",
                    CreatedByDoctorId: "DR-MEND-01",
                    ExpectedSyncVersion: 1));
                ephemeralHandshakeId = created.HandshakeId;
                handshakeService.AcknowledgeDirective(ephemeralHandshakeId, "NR-MEND-01", reportedSyncVersion: 999);
            }

            mainVm.SyncMonitorVm.RefreshDiagnostics();
            mainVm.SyncMonitorVm.FocusDirective(ephemeralHandshakeId);
            mainVm.SyncMonitorVm.ClinicianStaffId = "DR-MEND-01";
            mainVm.SyncMonitorVm.ResolutionNotes = "Wave 3 confirmation headline check resolved.";
            mainVm.SyncMonitorVm.ResolveConflictCommand.Execute(null);
            PumpUi(window);

            string headline = mainVm.SyncMonitorVm.ResolutionFeedbackMessage ?? string.Empty;
            string detail = mainVm.SyncMonitorVm.LastResolvedStatusText;
            Assert(headline.StartsWith("✓ Order Confirmed by DR-MEND-01", StringComparison.Ordinal) &&
                   !headline.Contains(ephemeralHandshakeId, StringComparison.Ordinal),
                $"ResolutionFeedbackMessage headline must read '✓ Order Confirmed by DR-MEND-01 (v...)' without Ref HS ID, got '{headline}'.");
            Assert(detail.StartsWith("Ref HS-", StringComparison.Ordinal) &&
                   !detail.Contains("SyncVersion", StringComparison.Ordinal),
                $"LastResolvedStatusText must hold clean 'Ref HS-...' summary without SyncVersion, got '{detail}'.");
        });

        // -----------------------------------------------------------------
        // ITERATION 40: SafetyRx Concise Bath, ADE Watermarks & Zero SyncVersion Leak
        // -----------------------------------------------------------------
        Console.WriteLine("[ITER 40/41] SafetyRx Concise Bath Summary, Short ADE Watermarks & Handshake Sanitation...");
        CaptureStep("ITER40:SafetyRxWave3", () =>
        {
            mainVm.BannerVm.SelectPatientById(patientAlphaId, notifyListeners: true);
            mainVm.SelectWorkspaceTab(WorkspaceTab.SafetyRxWorkbench);
            PumpUi(window);

            Assert(mainVm.SafetyRxVm.FormattedActiveBathSummary.Contains("Bath K 2, Ca 1.25, Na 138, HCO3 34 mEq/L", StringComparison.Ordinal),
                $"FormattedActiveBathSummary must use concise bath electrolytes without mid-unit wrapping, got '{mainVm.SafetyRxVm.FormattedActiveBathSummary}'.");

            var medExpander = FindRequired<Expander>(safetyRxView, "MedicationAnemiaExpander");
            Assert(!medExpander.IsExpanded, "MedicationAnemiaExpander must default collapsed on SafetyRxWorkbenchView in Wave 4.");
            medExpander.IsExpanded = true;
            PumpUi(window);

            var rxBox = FindRequired<TextBox>(safetyRxView, "CandidateConcomitantRxTextBox");
            var crclBox = FindRequired<TextBox>(safetyRxView, "EstimatedCrClTextBox");
            Assert(rxBox.IsEffectivelyVisible && crclBox.IsEffectivelyVisible,
                "ADE inputs must become visible when MedicationAnemiaExpander is expanded.");
            Assert(rxBox.Watermark == "e.g. 1191 or 7052" && crclBox.Watermark == "e.g. 12",
                $"ADE watermarks must match Wave 4 concise example labels, got '{rxBox.Watermark}' / '{crclBox.Watermark}'.");

            CrossScreenGeometryAuditor.AuditNoDeveloperJargon(safetyRxView, "ITER40:SafetyRxWave3");

            medExpander.IsExpanded = false;
            PumpUi(window);
        });

        // -----------------------------------------------------------------
        // ITERATION 41: ShiftGrid Rounded Inset 4px Semantic Edge Accent Bar
        // -----------------------------------------------------------------
        Console.WriteLine("[ITER 41/41] ShiftGrid Rounded 4px Inset Edge Bar...");
        CaptureStep("ITER41:ShiftGridWave3", () =>
        {
            mainVm.SelectWorkspaceTab(WorkspaceTab.ShiftGrid);
            mainVm.ShiftGridVm.SetRiskFilterCommand.Execute("All");
            mainVm.ShiftGridVm.FilterText = string.Empty;
            PumpUi(window);

            var bedTiles = FindRequired<ItemsControl>(shiftGridView, "BedTilesItemsControl");
            Border? alphaCard = bedTiles.GetVisualDescendants()
                .OfType<Border>()
                .FirstOrDefault(b => b.DataContext is ShiftBedTileItem tile && tile.PatientId == patientAlphaId);
            Assert(alphaCard is not null && alphaCard.ClipToBounds,
                "BED-UX-01 card Border must set ClipToBounds=True for smooth CornerRadius=12 framing.");

            Grid? alphaGrid = alphaCard!.Child as Grid;
            Border? insetEdge = alphaGrid?.Children.OfType<Border>().FirstOrDefault(b => Grid.GetColumn(b) == 0);
            Assert(insetEdge is not null &&
                   Math.Abs(insetEdge!.Width - 4.0) < 0.1 &&
                   insetEdge.CornerRadius.TopLeft >= 2.0 &&
                   insetEdge.Margin.Left >= 1.5,
                $"BED-UX-01 semantic edge bar must be inset 4px pill with CornerRadius=2, got width={insetEdge?.Width}, corner={insetEdge?.CornerRadius}, margin={insetEdge?.Margin}.");
        });

        // Restore clean state before viewport sweeps
        mainVm.BannerVm.SelectPatientById(patientAlphaId, notifyListeners: true);
        mainVm.SelectWorkspaceTab(WorkspaceTab.ShiftGrid);
        PumpUi(window);
    }

    public static void AuditViewportTabWave3Invariants(
        MainWindow window,
        WorkspaceTab tab,
        double width,
        string viewportLabel)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (tab == WorkspaceTab.SafetyRxWorkbench)
        {
            var safetyRxView = FindRequiredDescendant<SafetyRxWorkbenchView>(window);
            var medExpander = FindRequired<Expander>(safetyRxView, "MedicationAnemiaExpander");
            Assert(!medExpander.IsExpanded,
                $"[{viewportLabel}] MedicationAnemiaExpander must start collapsed before viewport audit.");

            medExpander.IsExpanded = true;
            PumpUi(window);

            var rxBox = FindRequired<TextBox>(safetyRxView, "CandidateConcomitantRxTextBox");
            var crclBox = FindRequired<TextBox>(safetyRxView, "EstimatedCrClTextBox");

            double minRxWidth = width < 1200.0 ? 95.0 : 140.0;
            double minCrClWidth = width < 1200.0 ? 80.0 : 115.0;
            Assert(rxBox.Bounds.Width >= minRxWidth && crclBox.Bounds.Width >= minCrClWidth,
                $"[{viewportLabel}] ADE TextBoxes too narrow for concise watermarks (rx={rxBox.Bounds.Width:0.#}px >= {minRxWidth}, crcl={crclBox.Bounds.Width:0.#}px >= {minCrClWidth} at width={width}).");

            medExpander.IsExpanded = false;
            PumpUi(window);
        }
    }

    private static void PumpUi(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static T FindRequired<T>(Control root, string name) where T : Control
    {
        var match = root.FindControl<T>(name);
        if (match is null)
        {
            throw new InvalidOperationException($"Required control '{name}' ({typeof(T).Name}) was not found under {root.GetType().Name}.");
        }
        return match;
    }

    private static T FindRequiredDescendant<T>(Visual root) where T : Visual
    {
        var match = root.GetVisualDescendants().OfType<T>().FirstOrDefault();
        if (match is null)
        {
            throw new InvalidOperationException($"Required descendant of type {typeof(T).Name} was not found.");
        }
        return match;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"[ASSERTION FAILED] {message}");
        }
    }
}
