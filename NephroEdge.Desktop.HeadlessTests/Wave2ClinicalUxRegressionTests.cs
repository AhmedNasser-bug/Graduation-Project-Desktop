using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using NephroEdge.Desktop.Controls;
using NephroEdge.Desktop.Infrastructure;
using NephroEdge.Desktop.ViewModels;
using NephroEdge.Desktop.Views;
using Persistence_Unit.Entities;
using Services_Unit.Core.Contracts;
using Services_Unit.Core.Interfaces;

namespace NephroEdge.Desktop.HeadlessTests;

/// <summary>
/// Wave 2 visual-polish, clinical hierarchy, ISMP sanitation, custom card selection,
/// non-red baseline KPI, and interactive chart tooltip geometry regression checks.
/// </summary>
public static class Wave2ClinicalUxRegressionTests
{
    public static void RunPreViewportWave2Checks(
        MainWindow window,
        MainWindowViewModel mainVm,
        IServiceScopeFactory scopeFactory,
        string patientAlphaId,
        string patientBetaId,
        string patientNomId,
        string longitudinalMendeleyPatientId,
        List<string> defectAccumulator)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(mainVm);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(defectAccumulator);

        var stickyBanner = FindRequired<StickyPatientBanner>(window, "StickyBanner");
        var shiftGridView = FindRequiredDescendant<ShiftGridView>(window);
        var analyticsView = FindRequiredDescendant<AnalyticsView>(window);
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
        // ITERATION 34: StickyBanner Access Hygiene & ShiftGrid Permanent Edge Bar
        // -----------------------------------------------------------------
        Console.WriteLine("[ITER 34/38] Banner Underscore-Free Access & ShiftGrid Permanent 4px Edge Accent...");
        CaptureStep("ITER34:BannerAndShiftEdge", () =>
        {
            mainVm.BannerVm.SelectPatientById(patientAlphaId, notifyListeners: true);
            mainVm.ShiftGridVm.SetRiskFilterCommand.Execute("All");
            mainVm.ShiftGridVm.FilterText = string.Empty;
            mainVm.SelectWorkspaceTab(WorkspaceTab.ShiftGrid);
            PumpUi(window);

            var bannerAccess = FindRequired<TextBlock>(stickyBanner, "BannerAccessText");
            string accessText = bannerAccess.Text ?? string.Empty;
            Assert(!accessText.Contains('_', StringComparison.Ordinal) &&
                   accessText.Contains("Left Forearm Radiocephalic", StringComparison.Ordinal),
                $"BannerAccessText must render underscore-free humanized access, got '{accessText}'.");

            var bedTiles = FindRequired<ItemsControl>(shiftGridView, "BedTilesItemsControl");
            Border? alphaCard = FindBedCardBorder(bedTiles, patientAlphaId);
            Border? betaCard = FindBedCardBorder(bedTiles, patientBetaId);
            Assert(alphaCard is not null && betaCard is not null,
                "Expected rendered BED-UX-01 and BED-UX-02 cards in ShiftGrid.");

            var alphaTile = mainVm.ShiftGridVm.FilteredBeds.First(b => b.PatientId == patientAlphaId);
            var betaTile = mainVm.ShiftGridVm.FilteredBeds.First(b => b.PatientId == patientBetaId);
            Assert(alphaTile.IsSelected && !betaTile.IsSelected &&
                   alphaTile.PrimaryActionText == "Review Alert →" &&
                   betaTile.PrimaryActionText == "Review Alert →",
                $"ShiftGrid selection/PrimaryActionText mismatch: AlphaSelected={alphaTile.IsSelected}, AlphaCTA='{alphaTile.PrimaryActionText}', BetaCTA='{betaTile.PrimaryActionText}'.");
            Assert(!Equals(alphaTile.CardBoxShadow, betaTile.CardBoxShadow),
                "Selected BED-UX-01 card must display outer teal CardBoxShadow distinct from unselected cards.");

            var alphaButtons = alphaCard!.GetVisualDescendants().OfType<Button>().ToList();
            Button? alphaPrimaryBtn = alphaButtons.FirstOrDefault(b => string.Equals(b.Content?.ToString(), "Review Alert →", StringComparison.Ordinal));
            Assert(alphaButtons.Count == 1 && alphaPrimaryBtn is not null,
                $"Each ShiftGrid bed card must render exactly 1 contextual CTA ('Review Alert →'), found count={alphaButtons.Count}.");

            Grid? alphaRootGrid = alphaCard!.Child as Grid;
            Border? edgeBar = alphaRootGrid?.Children.OfType<Border>().FirstOrDefault(b => Grid.GetColumn(b) == 0);
            Assert(edgeBar is not null &&
                   edgeBar!.Bounds.Width >= 3.5 &&
                   edgeBar.Background is ISolidColorBrush edgeBrush && edgeBrush.Color.ToUInt32() == 0xFFD92D20,
                $"Selected HighRed bed must keep permanent >= 3.5px HighRed (#D92D20) edge bar (width={edgeBar?.Bounds.Width}, bg={edgeBar?.Background}).");
        });

        // -----------------------------------------------------------------
        // ITERATION 35: Analytics Non-Red Tab-2 KPIs, Initial Baseline & Clean Status
        // -----------------------------------------------------------------
        Console.WriteLine("[ITER 35/38] Analytics Tab-2 KPIs (AVG POST-SBP/UF/spKt/V), Initial Baseline & Clean Lab Status...");
        CaptureStep("ITER35:AnalyticsWave2", () =>
        {
            mainVm.BannerVm.SelectPatientById(longitudinalMendeleyPatientId, notifyListeners: true);
            mainVm.SelectWorkspaceTab(WorkspaceTab.PatientAnalytics);
            mainVm.AnalyticsVm.SelectSessionsTabCommand.Execute(null);
            PumpUi(window);

            Assert(mainVm.AnalyticsVm.LowestNadirSeverityLevel == "Normal",
                $"Longitudinal patient PT-MEND-0018 (>=100 mmHg nadir) must have LowestNadirSeverityLevel='Normal', got '{mainVm.AnalyticsVm.LowestNadirSeverityLevel}' ({mainVm.AnalyticsVm.SessionLowestNadirText}).");

            foreach (string kpiHeaderText in new[] { "AVG POST-SBP", "AVG UF", "LAST spKt/V" })
            {
                TextBlock? header = analyticsView.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .FirstOrDefault(tb => tb.IsEffectivelyVisible && string.Equals(tb.Text, kpiHeaderText, StringComparison.Ordinal));
                Assert(header is not null, $"Could not locate visible '{kpiHeaderText}' KPI chip on Sessions & Kt/V tab.");
            }

            TextBlock? ktvHeader = analyticsView.GetVisualDescendants()
                .OfType<TextBlock>()
                .FirstOrDefault(tb => tb.IsEffectivelyVisible && string.Equals(tb.Text, "LAST spKt/V", StringComparison.Ordinal));
            Border? ktvCard = ktvHeader?.FindAncestorOfType<Border>();
            TextBlock? ktvValueText = ktvCard?.GetVisualDescendants()
                .OfType<TextBlock>()
                .FirstOrDefault(tb => !string.Equals(tb.Text, "LAST spKt/V", StringComparison.Ordinal));
            Assert(ktvValueText is not null && !string.IsNullOrWhiteSpace(ktvValueText.Text),
                "Could not locate LAST spKt/V value TextBlock in AnalyticsView.");
            if (mainVm.AnalyticsVm.LatestKtVSeverityLevel == "Normal" && ktvValueText!.Foreground is ISolidColorBrush ktvBrush)
            {
                uint c = ktvBrush.Color.ToUInt32();
                Assert(c == 0xFF0F766E,
                    $"Normal LAST spKt/V ('{ktvValueText.Text}') must render Clinical Teal #0F766E (non-red), not 0x{c:X8}.");
            }

            TextBlock? avgSbpHeader = analyticsView.GetVisualDescendants()
                .OfType<TextBlock>()
                .FirstOrDefault(tb => tb.IsEffectivelyVisible && string.Equals(tb.Text, "AVG POST-SBP", StringComparison.Ordinal));
            TextBlock? avgSbpValue = avgSbpHeader?.FindAncestorOfType<Border>()?.GetVisualDescendants()
                .OfType<TextBlock>()
                .FirstOrDefault(tb => !string.Equals(tb.Text, "AVG POST-SBP", StringComparison.Ordinal));
            if (avgSbpValue?.Foreground is ISolidColorBrush sbpBrush)
            {
                Assert(sbpBrush.Color.ToUInt32() == 0xFF0F172A,
                    $"AVG POST-SBP KPI value must render Slate #0F172A, not 0x{sbpBrush.Color.ToUInt32():X8}.");
            }

            // Switch back to Blood Labs Tab 1 for single-reading baseline & lab entry checks
            mainVm.AnalyticsVm.SelectLabsTabCommand.Execute(null);
            PumpUi(window);

            // Single-reading patient should display "Initial baseline (1 reading)" (never "Delta Baseline · Baseline")
            mainVm.BannerVm.SelectPatientById(patientBetaId, notifyListeners: true);
            PumpUi(window);
            mainVm.AnalyticsVm.SelectedLoincFilter = "718-7";
            PumpUi(window);
            Assert(mainVm.AnalyticsVm.SelectedLabSummaryText.Contains("Initial baseline (1 reading)", StringComparison.Ordinal) &&
                   !mainVm.AnalyticsVm.SelectedLabSummaryText.Contains("Delta Baseline", StringComparison.Ordinal),
                $"Single Hb reading summary must say 'Initial baseline (1 reading)', got '{mainVm.AnalyticsVm.SelectedLabSummaryText}'.");
            mainVm.AnalyticsVm.SelectedLoincFilter = "ALL";
            PumpUi(window);

            // Saving a non-Potassium lab on PatientNom should emit bracket-free status message (expand AddLabDrawerBorder first)
            mainVm.BannerVm.SelectPatientById(patientNomId, notifyListeners: true);
            PumpUi(window);
            var toggleAddLabBtn = FindRequired<Button>(analyticsView, "ToggleAddLabDrawerButton");
            var addLabDrawer = FindRequired<Border>(analyticsView, "AddLabDrawerBorder");
            if (!addLabDrawer.IsVisible)
            {
                ClickControlCenter(window, toggleAddLabBtn);
            }
            Assert(addLabDrawer.IsEffectivelyVisible, "AddLabDrawerBorder must expand on ToggleAddLabDrawerButton click.");

            var nonPotCatalog = mainVm.AnalyticsVm.CatalogLoincItems.FirstOrDefault(c => c.LoincCode == "17861-6")
                ?? mainVm.AnalyticsVm.CatalogLoincItems.First(c => c.LoincCode != "2823-3" && c.LoincCode != "718-7");
            mainVm.AnalyticsVm.SelectedAddLabCatalog = nonPotCatalog;
            mainVm.AnalyticsVm.AddLabLoincCode = nonPotCatalog.LoincCode;
            mainVm.AnalyticsVm.AddLabPhase = "Pre-Dialysis";
            mainVm.AnalyticsVm.AddLabValue = "9.2";
            var addLabValueBox = FindRequired<TextBox>(analyticsView, "AddLabValueTextBox");
            addLabValueBox.Focus();
            PumpUi(window);
            PressKeyCombo(window, Key.Enter, RawInputModifiers.None);

            string saveMsg = mainVm.AnalyticsVm.LabSaveStatusMessage;
            Assert(saveMsg.StartsWith("✓ Saved", StringComparison.Ordinal) &&
                   !saveMsg.Contains('[') && !saveMsg.Contains(']'),
                $"LabSaveStatusMessage must start with '✓ Saved' and omit square brackets, got '{saveMsg}'.");

            // Saving panic Potassium 6.5 mmol/L must flag Critical + IsLabSaveCritical (never "Normal Range")
            var potCatalog = mainVm.AnalyticsVm.CatalogLoincItems.First(c => c.LoincCode == "2823-3");
            mainVm.AnalyticsVm.SelectedAddLabCatalog = potCatalog;
            mainVm.AnalyticsVm.AddLabLoincCode = "2823-3";
            mainVm.AnalyticsVm.AddLabPhase = "Pre-Dialysis";
            mainVm.AnalyticsVm.AddLabValue = "6.5";
            addLabValueBox.Focus();
            PumpUi(window);
            PressKeyCombo(window, Key.Enter, RawInputModifiers.None);

            string panicMsg = mainVm.AnalyticsVm.LabSaveStatusMessage;
            Assert(mainVm.AnalyticsVm.IsLabSaveCritical &&
                   panicMsg.Contains("Critical High Alert", StringComparison.Ordinal) &&
                   !panicMsg.Contains("Normal Range", StringComparison.OrdinalIgnoreCase),
                $"Saving panic Potassium 6.5 mmol/L must report 'Critical High Alert' with IsLabSaveCritical=true (not Normal Range), got IsCritical={mainVm.AnalyticsVm.IsLabSaveCritical}, msg='{panicMsg}'.");

            TextBlock? visibleCriticalStatus = analyticsView.GetVisualDescendants()
                .OfType<TextBlock>()
                .FirstOrDefault(tb => tb.IsEffectivelyVisible &&
                                      string.Equals(tb.Text, panicMsg, StringComparison.Ordinal));
            Assert(visibleCriticalStatus is not null &&
                   visibleCriticalStatus!.Foreground is ISolidColorBrush critBrush &&
                   critBrush.Color.ToUInt32() == 0xFFB91C1C,
                $"Critical lab save status must render visible Crimson #B91C1C TextBlock, got fg={visibleCriticalStatus?.Foreground}.");

            if (addLabDrawer.IsVisible)
            {
                ClickControlCenter(window, toggleAddLabBtn);
            }
        });

        // -----------------------------------------------------------------
        // ITERATION 36: SafetyRx ISMP Sanitation, Criterion 4 Pill Jump & Footer FHIR Toggle
        // -----------------------------------------------------------------
        Console.WriteLine("[ITER 36/38] SafetyRx ISMP Sanitation, Safety Pill Scroll Jump & Footer FHIR Toggle...");
        CaptureStep("ITER36:SafetyRxWave2", () =>
        {
            mainVm.BannerVm.SelectPatientById(patientAlphaId, notifyListeners: true);
            mainVm.SelectWorkspaceTab(WorkspaceTab.SafetyRxWorkbench);
            var safetySv = FindRequired<ScrollViewer>(safetyRxView, "SafetyRxScrollViewer");
            safetySv.Offset = default;
            PumpUi(window);

            CrossScreenGeometryAuditor.AuditIsmpTypography(safetyRxView, "ITER36:SafetyRxWave2");

            var safetyPill = FindRequired<Border>(safetyRxView, "SafetyStatusPillBorder");
            var reasoningExpander = FindRequired<Expander>(safetyRxView, "ClinicalReasoningExpander");
            var criterionCard = FindRequired<Border>(safetyRxView, "Criterion4Card");
            Assert(!reasoningExpander.IsExpanded, "ClinicalReasoningExpander must start collapsed before SafetyStatusPillBorder click.");
            ClickControlCenter(window, safetyPill);
            Assert(reasoningExpander.IsExpanded && criterionCard.IsEffectivelyVisible,
                "Clicking SafetyStatusPillBorder must expand ClinicalReasoningExpander and reveal Criterion4Card.");

            var toggleFhirBtn = FindRequired<Button>(safetyRxView, "ToggleFhirDrawerButton");
            var exportFhirBtn = FindRequired<Button>(safetyRxView, "ExportFhirBundleButton");
            var handshakesItems = FindRequired<ItemsControl>(safetyRxView, "OrderHandshakesItemsControl");
            CrossScreenGeometryAuditor.AuditVerticalOrder(exportFhirBtn, handshakesItems, window, "ITER36:FhirFooterBeforeHandshakes");

            bool initialExpanded = mainVm.SafetyRxVm.IsFhirDrawerExpanded;
            string expectedInitialBtnText = initialExpanded ? "Hide FHIR JSON" : "Show FHIR JSON";
            Assert(string.Equals(mainVm.SafetyRxVm.FhirToggleButtonText, expectedInitialBtnText, StringComparison.Ordinal),
                $"Expected FhirToggleButtonText='{expectedInitialBtnText}', got '{mainVm.SafetyRxVm.FhirToggleButtonText}'.");

            toggleFhirBtn.BringIntoView();
            PumpUi(window);
            ClickControlCenter(window, toggleFhirBtn);
            Assert(mainVm.SafetyRxVm.IsFhirDrawerExpanded != initialExpanded,
                "Clicking ToggleFhirDrawerButton must flip IsFhirDrawerExpanded.");

            if (mainVm.SafetyRxVm.IsFhirDrawerExpanded)
            {
                var fhirBox = FindRequired<TextBox>(safetyRxView, "FhirJsonViewerBox");
                Assert(fhirBox.IsEffectivelyVisible, "Expanded FHIR JSON viewer must be effectively visible.");
                ClickControlCenter(window, toggleFhirBtn);
                Assert(!mainVm.SafetyRxVm.IsFhirDrawerExpanded &&
                       mainVm.SafetyRxVm.FhirToggleButtonText == "Show FHIR JSON",
                    "Clicking ToggleFhirDrawerButton again must collapse drawer and show 'Show FHIR JSON'.");
            }

            reasoningExpander.IsExpanded = false;
            safetySv.Offset = default;
            PumpUi(window);
        });

        // -----------------------------------------------------------------
        // ITERATION 37: SyncMonitor Custom Handshake Card & Amber/Cyan Inspector
        // -----------------------------------------------------------------
        Console.WriteLine("[ITER 37/38] SyncMonitor Custom .handshake-card Style, Dynamic Headers & Bridge Pill...");
        CaptureStep("ITER37:SyncMonitorWave2", () =>
        {
            mainVm.BannerVm.SelectPatientById(patientBetaId, notifyListeners: true);
            mainVm.SelectWorkspaceTab(WorkspaceTab.SyncMonitor);
            PumpUi(window);

            CrossScreenGeometryAuditor.AuditNoDeveloperJargon(syncMonitorView, "ITER37:SyncMonitor");
            CrossScreenGeometryAuditor.AuditNoRedOnTechnicalSync(syncMonitorView, "ITER37:SyncMonitor");

            var queueBox = FindRequired<ListBox>(syncMonitorView, "ConflictQueueListBox");
            var conflictFormPanel = FindRequired<StackPanel>(syncMonitorView, "ConflictResolutionFormPanel");
            var calmStatusCard = FindRequired<Border>(syncMonitorView, "CalmOrderStatusCard");
            var techExpander = FindRequired<Expander>(syncMonitorView, "TechnicalSyncExpander");
            var localhostBtn = FindRequired<Button>(syncMonitorView, "LocalhostAdbButton");
            var clinicLanBtn = FindRequired<Button>(syncMonitorView, "ClinicLanButton");
            var toggleBindBtn = FindRequired<Button>(syncMonitorView, "ToggleBindModeButton");

            Assert(!techExpander.IsExpanded, "TechnicalSyncExpander must start collapsed on SyncMonitor.");
            techExpander.IsExpanded = true;
            PumpUi(window);
            Assert(localhostBtn.Classes.Contains("active-bridge") && !clinicLanBtn.Classes.Contains("active-bridge"),
                "USB ADB LocalhostAdbButton must carry .active-bridge when loopback host is active.");
            Assert(!toggleBindBtn.IsVisible,
                "Legacy redundant ToggleBindModeButton must stay hidden inside segmented TechnicalSyncExpander bar.");
            techExpander.IsExpanded = false;
            PumpUi(window);

            OrderHandshakeRecord? pendingBetaItem = mainVm.SyncMonitorVm.ConflictQueueItems
                .FirstOrDefault(h => h.PatientId == patientBetaId && h.Status == "PENDING_ACK");
            Assert(pendingBetaItem is not null, "Expected PENDING_ACK directive for PatientBeta in ConflictQueueItems.");

            mainVm.SyncMonitorVm.SelectedConflict = pendingBetaItem;
            PumpUi(window);

            Assert(mainVm.SyncMonitorVm.IsSelectedItemWaiting &&
                   !mainVm.SyncMonitorVm.IsConflictResolutionVisible &&
                   mainVm.SyncMonitorVm.IsCalmStatusCardVisible &&
                   !conflictFormPanel.IsVisible && calmStatusCard.IsVisible &&
                   mainVm.SyncMonitorVm.CalmStatusCardTitle == "Waiting for Bedside Confirmation",
                $"Waiting directive must show CalmOrderStatusCard and hide ConflictResolutionFormPanel (title='{mainVm.SyncMonitorVm.CalmStatusCardTitle}').");
            Assert(mainVm.SyncMonitorVm.RightPaneHeaderTitle == "ORDER STATUS & CONFIRMATION",
                $"Expected Cyan awaiting-ack header 'ORDER STATUS & CONFIRMATION', got '{mainVm.SyncMonitorVm.RightPaneHeaderTitle}'.");
            Assert(mainVm.SyncMonitorVm.SelectedConflictPatientNameDisplay.Contains("Karim", StringComparison.Ordinal) &&
                   mainVm.SyncMonitorVm.SelectedConflictPatientIdsDisplay.Contains("MRN-UX-BETA", StringComparison.Ordinal) &&
                   mainVm.SyncMonitorVm.SelectedConflictPatientIdsDisplay.Contains(patientBetaId, StringComparison.Ordinal) &&
                   mainVm.SyncMonitorVm.SelectedConflictFooterDisplay.StartsWith("Ref ", StringComparison.Ordinal) &&
                   mainVm.SyncMonitorVm.SelectedConflictFooterDisplay.Contains("· v", StringComparison.Ordinal),
                $"Inspector formatting check failed: name='{mainVm.SyncMonitorVm.SelectedConflictPatientNameDisplay}', ids='{mainVm.SyncMonitorVm.SelectedConflictPatientIdsDisplay}', footer='{mainVm.SyncMonitorVm.SelectedConflictFooterDisplay}'.");

            ListBoxItem? selectedContainer = queueBox.GetVisualDescendants()
                .OfType<ListBoxItem>()
                .FirstOrDefault(lbi => lbi.IsSelected);
            Assert(selectedContainer is not null, "Expected selected ListBoxItem container inside ConflictQueueListBox.");
            Border? selectedCard = selectedContainer!.GetVisualDescendants()
                .OfType<Border>()
                .FirstOrDefault(b => b.Classes.Contains("handshake-card"));
            Assert(selectedCard is not null &&
                   selectedCard!.Background is ISolidColorBrush cardBg &&
                   cardBg.Color.ToUInt32() == 0xFFECFEFF &&
                   selectedCard.BorderBrush is ISolidColorBrush cardBorder &&
                   cardBorder.Color.ToUInt32() == 0xFF0891B2,
                $"Selected .handshake-card must render soft #ECFEFF fill + #0891B2 border (no Fluent default blue), got bg={selectedCard?.Background}, border={selectedCard?.BorderBrush}.");

            ContentPresenter? selectedPresenter = selectedContainer!.GetVisualDescendants()
                .OfType<ContentPresenter>()
                .FirstOrDefault(cp => cp.Name == "PART_ContentPresenter");
            if (selectedPresenter?.Background is ISolidColorBrush cpBrush)
            {
                Assert(cpBrush.Color.A == 0,
                    $"Selected ListBoxItem PART_ContentPresenter must stay transparent so Fluent blue does not cover .handshake-card, got 0x{cpBrush.Color.ToUInt32():X8}.");
            }

            // Inject ephemeral conflict on PatientNom -> verify Amber CLINICIAN CONFLICT REVIEW -> resolve cleanly
            string ephemeralHandshakeId;
            using (var scope = scopeFactory.CreateScope())
            {
                var handshakeService = scope.ServiceProvider.GetRequiredService<IOrderHandshakeService>();
                var created = handshakeService.QueueDirective(new ClosedLoopDirectiveRequest(
                    PatientId: patientNomId,
                    SessionId: null,
                    PrescriptionId: null,
                    DirectiveType: "DRY_WEIGHT_UPDATE",
                    DirectiveSummary: "Transient Wave 2 conflict header verification",
                    IsmpFormattedDoseText: "74.5 kg",
                    CreatedByDoctorId: "DR-MEND-01",
                    ExpectedSyncVersion: 1));
                ephemeralHandshakeId = created.HandshakeId;
                handshakeService.AcknowledgeDirective(ephemeralHandshakeId, "NR-MEND-01", reportedSyncVersion: 999);
            }

            mainVm.SyncMonitorVm.RefreshDiagnostics();
            mainVm.SyncMonitorVm.FocusDirective(ephemeralHandshakeId);
            PumpUi(window);

            Assert(mainVm.SyncMonitorVm.IsSelectedItemConflict &&
                   mainVm.SyncMonitorVm.IsConflictResolutionVisible &&
                   conflictFormPanel.IsVisible && !calmStatusCard.IsVisible &&
                   mainVm.SyncMonitorVm.RightPaneHeaderTitle == "CLINICIAN CONFLICT REVIEW",
                $"Conflicted directive must reveal ConflictResolutionFormPanel and flip right pane to Amber 'CLINICIAN CONFLICT REVIEW', got '{mainVm.SyncMonitorVm.RightPaneHeaderTitle}'.");

            mainVm.SyncMonitorVm.ClinicianStaffId = "DR-MEND-01";
            mainVm.SyncMonitorVm.ResolutionNotes = "Wave 2 conflict header verified; clinician resolved.";
            mainVm.SyncMonitorVm.ResolveConflictCommand.Execute(null);
            PumpUi(window);
            Assert(!mainVm.SyncMonitorVm.ConflictQueueItems.Any(h => h.HandshakeId == ephemeralHandshakeId),
                "Ephemeral conflict test directive must resolve cleanly without polluting screenshot state.");
            Assert(!conflictFormPanel.IsVisible && calmStatusCard.IsVisible,
                "Resolving conflict must hide ConflictResolutionFormPanel and restore CalmOrderStatusCard.");
        });

        // Restore clean baseline for viewport sweeps
        mainVm.BannerVm.SelectPatientById(patientAlphaId, notifyListeners: true);
        mainVm.ShiftGridVm.SetRiskFilterCommand.Execute("All");
        mainVm.ShiftGridVm.FilterText = string.Empty;
        mainVm.SelectWorkspaceTab(WorkspaceTab.ShiftGrid);
        PumpUi(window);
    }

    public static void AuditViewportTabWave2Invariants(
        MainWindow window,
        MainWindowViewModel mainVm,
        WorkspaceTab tab,
        double width,
        double height,
        string viewportLabel)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(mainVm);

        // 1. Verify Left Rail Navigation subtitles never clip across all viewports
        string[] navButtonNames =
        {
            "NavShiftGridButton",
            "NavAnalyticsButton",
            "NavSafetyRxButton",
            "NavSyncMonitorButton"
        };

        foreach (string navName in navButtonNames)
        {
            var navBtn = FindRequired<Button>(window, navName);
            Assert(CrossScreenGeometryAuditor.TryGetBoundsInWindow(navBtn, window, out Rect btnRect),
                $"[{viewportLabel}:{tab}] Could not resolve bounds of '{navName}'.");

            foreach (TextBlock tb in navBtn.GetVisualDescendants().OfType<TextBlock>())
            {
                if (!tb.IsEffectivelyVisible || string.IsNullOrEmpty(tb.Text))
                {
                    continue;
                }

                Assert(CrossScreenGeometryAuditor.TryGetBoundsInWindow(tb, window, out Rect tbRect),
                    $"[{viewportLabel}:{tab}] Could not resolve TextBlock bounds inside '{navName}'.");
                Assert(tbRect.Left >= btnRect.Left - 1.5 &&
                       tbRect.Right <= btnRect.Right + 1.5 &&
                       tbRect.Top >= btnRect.Top - 1.5 &&
                       tbRect.Bottom <= btnRect.Bottom + 1.5,
                    $"[{viewportLabel}:{tab}] Nav text '{tb.Text}' clipped outside '{navName}' (tb={tbRect}, btn={btnRect}).");
            }
        }

        var navShiftBtn = FindRequired<Button>(window, "NavShiftGridButton");
        var navAnalyticsBtn = FindRequired<Button>(window, "NavAnalyticsButton");
        var navSafetyBtn = FindRequired<Button>(window, "NavSafetyRxButton");
        var navSyncBtn = FindRequired<Button>(window, "NavSyncMonitorButton");
        Assert(navShiftBtn.GetVisualDescendants().OfType<TextBlock>().Any(tb => tb.Text == "Active Shift") &&
               navAnalyticsBtn.GetVisualDescendants().OfType<TextBlock>().Any(tb => tb.Text == "Patient Trends") &&
               navSafetyBtn.GetVisualDescendants().OfType<TextBlock>().Any(tb => tb.Text == "Orders & Safety") &&
               navSyncBtn.GetVisualDescendants().OfType<TextBlock>().Any(tb => tb.Text == "Nurse Sync"),
            $"[{viewportLabel}:{tab}] Left rail must display calm single-line labels ('Active Shift', 'Patient Trends', 'Orders & Safety', 'Nurse Sync').");

        // 2. ShiftGrid single-row filter alignment (>=1366px), clean search watermark & permanent 4px left edge accent bar
        if (tab == WorkspaceTab.ShiftGrid)
        {
            var shiftGridView = FindRequiredDescendant<ShiftGridView>(window);
            var searchBox = FindRequired<TextBox>(shiftGridView, "SearchFilterTextBox");
            Assert(searchBox.Watermark == "Search bed or patient..." && searchBox.Bounds.Width >= 140.0,
                $"[{viewportLabel}] SearchFilterTextBox must use concise watermark 'Search bed or patient...' and width >= 140px (got '{searchBox.Watermark}', {searchBox.Bounds.Width:0.#}px).");

            if (width >= 1365.0)
            {
                var allBtn = FindRequired<Button>(shiftGridView, "FilterAllButton");
                var highBtn = FindRequired<Button>(shiftGridView, "FilterHighRedButton");
                var isoBtn = FindRequired<Button>(shiftGridView, "FilterIsolatedButton");

                CrossScreenGeometryAuditor.TryGetBoundsInWindow(searchBox, window, out Rect searchRect);
                CrossScreenGeometryAuditor.TryGetBoundsInWindow(allBtn, window, out Rect allRect);
                CrossScreenGeometryAuditor.TryGetBoundsInWindow(highBtn, window, out Rect highRect);
                CrossScreenGeometryAuditor.TryGetBoundsInWindow(isoBtn, window, out Rect isoRect);

                double maxTopDelta = Math.Max(
                    Math.Abs(searchRect.Top - allRect.Top),
                    Math.Max(Math.Abs(allRect.Top - highRect.Top), Math.Abs(allRect.Top - isoRect.Top)));
                Assert(maxTopDelta <= 8.0,
                    $"[{viewportLabel}] ShiftGrid search/filter strip must stay single-row (maxTopDelta={maxTopDelta:0.#}px).");
            }

            var bedTiles = FindRequired<ItemsControl>(shiftGridView, "BedTilesItemsControl");
            foreach (Border cardBorder in bedTiles.GetVisualDescendants()
                         .OfType<Border>()
                         .Where(b => b.DataContext is ShiftBedTileItem)
                         .Take(4))
            {
                if (cardBorder.Child is Grid rootGrid)
                {
                    Border? edgeBar = rootGrid.Children.OfType<Border>().FirstOrDefault(b => Grid.GetColumn(b) == 0);
                    Assert(edgeBar is not null && edgeBar.Bounds.Width >= 3.5 && edgeBar.Background is not null,
                        $"[{viewportLabel}] ShiftGrid bed card missing 4px Column-0 semantic edge accent bar.");
                }
            }
        }
    }

    public static void VerifyWave2HoverTooltipGeometry(
        MainWindow window,
        AnalyticsViewModel analyticsVm,
        AnalyticsView analyticsView)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(analyticsVm);
        ArgumentNullException.ThrowIfNull(analyticsView);

        Console.WriteLine("[ITER 38/38] Hover Tooltip Bounds & Panic Dot Halo Clearance...");
        analyticsVm.SelectLabsTabCommand.Execute(null);
        analyticsVm.IsAddLabDrawerOpen = false;
        PumpUi(window);

        var labChart = FindRequired<ClinicalTrendChartControl>(analyticsView, "LabTrendChart");
        labChart.BringIntoView();
        PumpUi(window);

        Point localHover = new(labChart.PlotBoundsRect.Right - 10.0, labChart.PlotBoundsRect.Center.Y);
        Point? winHover = labChart.TranslatePoint(localHover, window);
        Assert(winHover.HasValue, "Could not map LabTrendChart panic hover point to Window coordinates.");

        window.MouseMove(winHover!.Value, RawInputModifiers.None);
        PumpUi(window);
        using (var _ = window.CaptureRenderedFrame())
        {
        }

        Rect plot = labChart.PlotBoundsRect;
        Rect tip = labChart.LastTooltipRect;
        double rightmostDotX = plot.Right - 10.0;

        Assert(labChart.IsTooltipVisible && tip.Width > 40.0 && tip.Height > 20.0,
            $"Expected visible non-empty LastTooltipRect when hovering 6.5 panic point, got visible={labChart.IsTooltipVisible}, rect={tip}.");
        Assert(tip.Left >= plot.Left - 1.0 &&
               tip.Right <= plot.Right + 1.0 &&
               tip.Top >= plot.Top - 1.0 &&
               tip.Bottom <= plot.Bottom + 1.0,
            $"LastTooltipRect {tip} escaped PlotBoundsRect {plot}.");
        Assert(tip.Right <= rightmostDotX - 14.0,
            $"Rightmost panic tooltip must sit clear left of dot/halo (tip.Right={tip.Right:0.#}, dotX={rightmostDotX:0.#}).");

        window.MouseMove(new Point(16, 16), RawInputModifiers.None);
        PumpUi(window);
        Assert(!labChart.IsTooltipVisible && labChart.LastTooltipRect == default,
            "Moving pointer away from LabTrendChart must clear IsTooltipVisible and LastTooltipRect.");
        Console.WriteLine($"  -> PASS: Hover tooltip rect {tip} sits inside plot and clear left of panic dot X={rightmostDotX:0.#}.");
    }

    private static Border? FindBedCardBorder(ItemsControl bedTilesItems, string patientId)
    {
        return bedTilesItems.GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(b => b.DataContext is ShiftBedTileItem tile && tile.PatientId == patientId);
    }

    private static void ClickControlCenter(Window window, Control control)
    {
        Point center = CrossScreenGeometryAuditor.GetControlCenterInWindow(control, window);
        window.MouseMove(center, RawInputModifiers.None);
        PumpUi(window);
        window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
        PumpUi(window);
        window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
        PumpUi(window);
    }

    private static void PressKeyCombo(Window window, Key key, RawInputModifiers modifiers)
    {
        PhysicalKey physical = key switch
        {
            Key.Enter => PhysicalKey.Enter,
            _ => PhysicalKey.None
        };

        window.KeyPress(key, modifiers, physical, null);
        PumpUi(window);
        window.KeyRelease(key, modifiers, physical, null);
        PumpUi(window);
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
