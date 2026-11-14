using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
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
using Persistence_Unit.Interfaces;
using Services_Unit.Core.Interfaces;

namespace NephroEdge.Desktop.HeadlessTests;

/// <summary>
/// Wave 1 heavy functional, interaction, clinical-UX, jargon-hygiene, above-the-fold priority,
/// keyboard shortcut, live cross-tab sync, and interactive Skia chart regression checks.
/// </summary>
public static class Wave1ClinicalUxRegressionTests
{
    public static void RunPreViewportWave1Checks(
        MainWindow window,
        MainWindowViewModel mainVm,
        IServiceScopeFactory scopeFactory,
        string patientAlphaId,
        string patientAlphaMrn,
        string patientBetaId,
        string patientBetaMrn,
        string patientNomId,
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
        var shellSyncCard = FindRequired<Border>(window, "ShellSyncIndicatorCard");

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
        // ITERATION 25: Mendeley 12-Bed Distinct Patient Seed & Clean Names
        // -----------------------------------------------------------------
        Console.WriteLine("[ITER 25/33] Mendeley 12-Bed Distinct Patient Seed & Clean Naming Audit...");
        mainVm.SelectWorkspaceTab(WorkspaceTab.ShiftGrid);
        mainVm.ShiftGridVm.SetRiskFilterCommand.Execute("All");
        mainVm.ShiftGridVm.FilterText = string.Empty;
        PumpUi(window);

        var mendeleyBeds = mainVm.ShiftGridVm.AllBedTiles
            .Where(b => !b.PatientId.StartsWith("PT-UX-", StringComparison.Ordinal))
            .ToList();

        Assert(mendeleyBeds.Count == 12,
            $"Expected 12 active Mendeley shift beds, found {mendeleyBeds.Count}.");
        int distinctMendeleyPatientCount = mendeleyBeds.Select(b => b.PatientId).Distinct(StringComparer.Ordinal).Count();
        Assert(distinctMendeleyPatientCount == 12,
            $"Expected 12 DISTINCT patients across Mendeley BED-01..BED-12, found only {distinctMendeleyPatientCount}.");

        foreach (var tile in mainVm.ShiftGridVm.AllBedTiles)
        {
            Assert(!tile.PatientFullName.Contains('#', StringComparison.Ordinal),
                $"Patient name on {tile.ChairOrBedNumber} contains debug '#' suffix: '{tile.PatientFullName}'.");
        }

        foreach (var patient in mainVm.BannerVm.AllPatients)
        {
            Assert(!patient.FullName.Contains('#', StringComparison.Ordinal),
                $"PatientSummary '{patient.PatientId}' contains debug '#' suffix: '{patient.FullName}'.");
        }

        foreach (var nomMendeley in mendeleyBeds.Where(b => b.IsNominal))
        {
            Assert(nomMendeley.PreSystolicBp >= 100,
                $"Non-IDH Mendeley bed {nomMendeley.ChairOrBedNumber} ({nomMendeley.PatientId}) has unrealistic PreSBP={nomMendeley.PreSystolicBp}.");
        }

        Console.WriteLine($"  -> PASS: 12 distinct Mendeley shift patients verified across BED-01..BED-12; zero '#' suffixes; non-IDH PreSBP >= 100.");

        // -----------------------------------------------------------------
        // ITERATION 26: Zero Developer/DB Jargon & Full-App ISMP Audit
        // -----------------------------------------------------------------
        Console.WriteLine("[ITER 26/33] Zero Developer/DB Jargon & Full-App ISMP + Red-Reserve Audit (All 4 Tabs)...");
        mainVm.BannerVm.SelectPatientById(patientAlphaId, notifyListeners: true);
        PumpUi(window);
        CaptureStep("ITER26:Banner", () =>
        {
            CrossScreenGeometryAuditor.AuditNoDeveloperJargon(stickyBanner, "StickyPatientBanner");
            CrossScreenGeometryAuditor.AuditIsmpTypography(stickyBanner, "StickyPatientBanner");
        });

        foreach (WorkspaceTab tab in Enum.GetValues<WorkspaceTab>())
        {
            mainVm.SelectWorkspaceTab(tab);
            PumpUi(window);

            Control view = tab switch
            {
                WorkspaceTab.ShiftGrid => shiftGridView,
                WorkspaceTab.PatientAnalytics => analyticsView,
                WorkspaceTab.SafetyRxWorkbench => safetyRxView,
                WorkspaceTab.SyncMonitor => syncMonitorView,
                _ => throw new ArgumentOutOfRangeException(nameof(tab))
            };

            CaptureStep($"ITER26:{tab}:Jargon", () => CrossScreenGeometryAuditor.AuditNoDeveloperJargon(view, $"Tab:{tab}"));
            CaptureStep($"ITER26:{tab}:ISMP", () => CrossScreenGeometryAuditor.AuditIsmpTypography(view, $"Tab:{tab}"));
        }

        var shiftSyncBtn = FindRequired<Button>(shiftGridView, "ShiftSyncNoticeBanner");
        CaptureStep("ITER26:RedReserve", () =>
        {
            CrossScreenGeometryAuditor.AuditNoRedOnTechnicalSync(shellSyncCard, "Shell.ShellSyncIndicatorCard");
            CrossScreenGeometryAuditor.AuditNoRedOnTechnicalSync(shiftSyncBtn, "ShiftGrid.ShiftSyncNoticeBanner");
            CrossScreenGeometryAuditor.AuditNoRedOnTechnicalSync(syncMonitorView, "SyncMonitorView");
        });
        Console.WriteLine("  -> Checked jargon, ISMP typography, and Red-reserve across all 4 tabs.");

        // -----------------------------------------------------------------
        // ITERATION 27: Real Keyboard Shortcuts (Ctrl+1..4, F5, Ctrl+R, Esc, Enter)
        // -----------------------------------------------------------------
        Console.WriteLine("[ITER 27/33] Keyboard Shortcuts (Ctrl+1..4, F5/Ctrl+R, Esc clear, Enter submit)...");
        CaptureStep("ITER27:KeyboardShortcuts", () =>
        {
            PressKeyCombo(window, Key.D2, RawInputModifiers.Control);
            Assert(mainVm.ActiveTab == WorkspaceTab.PatientAnalytics,
                $"Ctrl+2 must switch to PatientAnalytics, got {mainVm.ActiveTab}.");

            PressKeyCombo(window, Key.D3, RawInputModifiers.Control);
            Assert(mainVm.ActiveTab == WorkspaceTab.SafetyRxWorkbench,
                $"Ctrl+3 must switch to SafetyRxWorkbench, got {mainVm.ActiveTab}.");

            PressKeyCombo(window, Key.D4, RawInputModifiers.Control);
            Assert(mainVm.ActiveTab == WorkspaceTab.SyncMonitor,
                $"Ctrl+4 must switch to SyncMonitor, got {mainVm.ActiveTab}.");

            PressKeyCombo(window, Key.D1, RawInputModifiers.Control);
            Assert(mainVm.ActiveTab == WorkspaceTab.ShiftGrid,
                $"Ctrl+1 must switch to ShiftGrid, got {mainVm.ActiveTab}.");

            PressKeyCombo(window, Key.F5, RawInputModifiers.None);
            PressKeyCombo(window, Key.R, RawInputModifiers.Control);

            // Esc clears ShiftGrid search filter
            var searchBox = FindRequired<TextBox>(shiftGridView, "SearchFilterTextBox");
            searchBox.Focus();
            PumpUi(window);
            mainVm.ShiftGridVm.FilterText = "MRN-UX-BETA";
            PumpUi(window);
            Assert(mainVm.ShiftGridVm.FilteredBeds.Count == 1 &&
                   mainVm.ShiftGridVm.FilteredBeds[0].PatientId == patientBetaId,
                $"FilterText='MRN-UX-BETA' expected 1 match (PatientBeta), got {mainVm.ShiftGridVm.FilteredBeds.Count}.");

            PressKeyCombo(window, Key.Escape, RawInputModifiers.None);
            Assert(string.IsNullOrEmpty(mainVm.ShiftGridVm.FilterText) &&
                   mainVm.ShiftGridVm.FilteredBeds.Count == mainVm.ShiftGridVm.TotalBedCount,
                "Esc inside SearchFilterTextBox must clear search and restore all shift beds.");

            // Enter in SearchFilterTextBox selects first matching bed
            mainVm.ShiftGridVm.FilterText = "Amira";
            searchBox.Focus();
            PumpUi(window);
            PressKeyCombo(window, Key.Enter, RawInputModifiers.None);
            Assert(mainVm.BannerVm.SelectedPatientId == patientAlphaId,
                "Enter inside SearchFilterTextBox with 'Amira' must select PatientAlpha.");
            mainVm.ShiftGridVm.FilterText = string.Empty;
            PumpUi(window);

            // Enter in Analytics AddLabValueTextBox saves lab cleanly (use PatientNom to keep Alpha/Beta clean)
            mainVm.BannerVm.SelectPatientById(patientNomId, notifyListeners: true);
            PressKeyCombo(window, Key.D2, RawInputModifiers.Control);
            mainVm.AnalyticsVm.SelectLabsTabCommand.Execute(null);
            PumpUi(window);

            var toggleAddLabBtn = FindRequired<Button>(analyticsView, "ToggleAddLabDrawerButton");
            var addLabDrawer = FindRequired<Border>(analyticsView, "AddLabDrawerBorder");
            Assert(!addLabDrawer.IsVisible, "AddLabDrawerBorder must start collapsed on Patient Trends Tab 1.");
            ClickControlCenter(window, toggleAddLabBtn);
            Assert(addLabDrawer.IsEffectivelyVisible, "Clicking ToggleAddLabDrawerButton must reveal AddLabDrawerBorder.");

            var addLabValueBox = FindRequired<TextBox>(analyticsView, "AddLabValueTextBox");
            mainVm.AnalyticsVm.SelectedAddLabCatalog =
                mainVm.AnalyticsVm.CatalogLoincItems.FirstOrDefault(c => c.LoincCode == "2823-3");
            mainVm.AnalyticsVm.AddLabLoincCode = "2823-3";
            mainVm.AnalyticsVm.AddLabPhase = "Pre-Dialysis";
            mainVm.AnalyticsVm.AddLabValue = "4.8";
            addLabValueBox.Focus();
            PumpUi(window);
            PressKeyCombo(window, Key.Enter, RawInputModifiers.None);
            Assert(mainVm.AnalyticsVm.LabTrends.Any(t => t.LoincCode == "2823-3" && t.LatestValue.HasValue && Math.Abs(t.LatestValue.Value - 4.8) < 0.01),
                "Enter inside AddLabValueTextBox must persist Potassium 4.8 mmol/L.");
            ClickControlCenter(window, toggleAddLabBtn);

            // Switch to Tab 2 (Sessions & Kt/V), expand KtvCalculatorExpander, then Enter in KtVPostWeightTextBox computes Daugirdas II Kt/V
            var sessionsTabBtn = FindRequired<Button>(analyticsView, "SessionsTabButton");
            var labsTabBtn = FindRequired<Button>(analyticsView, "LabsTabButton");
            ClickControlCenter(window, sessionsTabBtn);
            var ktvExpander = FindRequired<Expander>(analyticsView, "KtvCalculatorExpander");
            Assert(!ktvExpander.IsExpanded, "KtvCalculatorExpander must start collapsed on Tab 2.");
            ktvExpander.IsExpanded = true;
            PumpUi(window);

            var ktvPostWeightBox = FindRequired<TextBox>(analyticsView, "KtVPostWeightTextBox");
            Assert(ktvPostWeightBox.IsEffectivelyVisible, "Expanding KtvCalculatorExpander must reveal Kt/V inputs.");
            mainVm.AnalyticsVm.KtVPreBun = "65";
            mainVm.AnalyticsVm.KtVPostBun = "19";
            mainVm.AnalyticsVm.KtVDurationHours = "4";
            mainVm.AnalyticsVm.KtVUfLiters = "2";
            mainVm.AnalyticsVm.KtVPostWeightKg = "72";
            ktvPostWeightBox.Focus();
            PumpUi(window);
            PressKeyCombo(window, Key.Enter, RawInputModifiers.None);
            Assert(mainVm.AnalyticsVm.KtVResult is { IsValid: true, IsAdequateKdoqiTarget: true },
                "Enter inside KtVPostWeightTextBox must compute valid adequate spKt/V.");
            ktvExpander.IsExpanded = false;
            ClickControlCenter(window, labsTabBtn);

            // Enter in SafetyRx ADE input triggers EvaluateSafetyCommand (after expanding MedicationAnemiaExpander)
            mainVm.BannerVm.SelectPatientById(patientAlphaId, notifyListeners: true);
            PressKeyCombo(window, Key.D3, RawInputModifiers.Control);
            var medExpander = FindRequired<Expander>(safetyRxView, "MedicationAnemiaExpander");
            Assert(!medExpander.IsExpanded, "MedicationAnemiaExpander must start collapsed on SafetyRx.");
            medExpander.IsExpanded = true;
            PumpUi(window);

            var adeRxBox = FindRequired<TextBox>(safetyRxView, "CandidateConcomitantRxTextBox");
            Assert(adeRxBox.IsEffectivelyVisible, "Expanding MedicationAnemiaExpander must reveal ADE Rx input.");
            mainVm.SafetyRxVm.CandidateConcomitantRxCui = "67108";
            mainVm.SafetyRxVm.EstimatedCrClText = "10";
            adeRxBox.Focus();
            PumpUi(window);
            PressKeyCombo(window, Key.Enter, RawInputModifiers.None);
            Assert(mainVm.SafetyRxVm.OverallSafetyBadgeText == "BlockedContraindication" &&
                   mainVm.SafetyRxVm.OverallSafetyDisplayText == "Contraindication Blocked",
                $"Enter inside CandidateConcomitantRxTextBox must evaluate ADE block, got '{mainVm.SafetyRxVm.OverallSafetyBadgeText}' / '{mainVm.SafetyRxVm.OverallSafetyDisplayText}'.");

            mainVm.SafetyRxVm.CandidateConcomitantRxCui = string.Empty;
            mainVm.SafetyRxVm.EstimatedCrClText = string.Empty;
            adeRxBox.Focus();
            PumpUi(window);
            PressKeyCombo(window, Key.Enter, RawInputModifiers.None);
            Assert(mainVm.SafetyRxVm.OverallSafetyBadgeText != "BlockedContraindication",
                "Clearing ADE probe and pressing Enter must clear Contraindication Blocked state.");
            medExpander.IsExpanded = false;
            PumpUi(window);
        });

        // -----------------------------------------------------------------
        // ITERATION 28: Whole-Card Click, Viewing Cue, Enriched Switcher & KPI Filter Clicks
        // -----------------------------------------------------------------
        Console.WriteLine("[ITER 28/33] Whole-Card Bed Click, Viewing Cue, Enriched Switcher & KPI Filter Clicks...");
        CaptureStep("ITER28:CardClickAndSwitcher", () =>
        {
            PressKeyCombo(window, Key.D1, RawInputModifiers.Control);
            window.FocusManager?.ClearFocus();
            PumpUi(window);

            var bedTilesItems = FindRequired<ItemsControl>(shiftGridView, "BedTilesItemsControl");
            Border? betaCardBorder = FindBedCardBorder(bedTilesItems, patientBetaId);
            Assert(betaCardBorder is not null, $"Could not find rendered bed card Border for '{patientBetaId}'.");

            TextBlock? betaNameLabel = betaCardBorder!.GetVisualDescendants()
                .OfType<TextBlock>()
                .FirstOrDefault(tb => tb.FindAncestorOfType<Button>() is null &&
                                      tb.Text is not null &&
                                      tb.Text.Contains("Karim", StringComparison.Ordinal));
            Assert(betaNameLabel is not null, "Could not locate Karim TextBlock inside BED-UX-02 card body.");

            betaCardBorder!.BringIntoView();
            PumpUi(window);
            ClickControlCenter(window, betaNameLabel!);

            var betaTile = mainVm.ShiftGridVm.FilteredBeds.First(b => b.PatientId == patientBetaId);
            var nomTile = mainVm.ShiftGridVm.FilteredBeds.First(b => b.PatientId == patientNomId);
            Assert(mainVm.BannerVm.SelectedPatientId == patientBetaId &&
                   betaTile.IsSelected &&
                   betaTile.SelectButtonLabel == "Viewing" &&
                   betaTile.PrimaryActionText == "Review Alert →" &&
                   !Equals(betaTile.CardBoxShadow, nomTile.CardBoxShadow),
                $"Whole-card click must select PatientBeta with SelectButtonLabel='Viewing' and selected CardBoxShadow, got selected={mainVm.BannerVm.SelectedPatientId}, label='{betaTile.SelectButtonLabel}'.");

            var bannerNameText = FindRequired<TextBlock>(stickyBanner, "BannerPatientNameText");
            var bannerMrnText = FindRequired<TextBlock>(stickyBanner, "BannerMrnText");
            Assert(bannerNameText.Text?.Contains("Karim", StringComparison.Ordinal) == true &&
                   bannerMrnText.Text?.Contains(patientBetaMrn, StringComparison.Ordinal) == true,
                "StickyPatientBanner must update live after whole-card click.");

            var switcherCombo = FindRequired<ComboBox>(stickyBanner, "PatientSwitcherComboBox");
            var alphaOption = mainVm.BannerVm.SwitcherItems.FirstOrDefault(o => o.PatientId == patientAlphaId);
            Assert(alphaOption is not null &&
                   alphaOption.BedLabel == "BED-UX-01" &&
                   alphaOption.AlarmGlyph == "▲" &&
                   alphaOption.IsIsolationRequired &&
                   alphaOption.IsolationTag == "HBV+",
                $"Enriched PatientSwitcherOption for PatientAlpha incomplete: Bed='{alphaOption?.BedLabel}', Glyph='{alphaOption?.AlarmGlyph}', Iso='{alphaOption?.IsolationTag}'.");

            switcherCombo.SelectedItem = alphaOption;
            PumpUi(window);
            Assert(mainVm.BannerVm.SelectedPatientId == patientAlphaId &&
                   bannerMrnText.Text?.Contains(patientAlphaMrn, StringComparison.Ordinal) == true,
                "Selecting enriched option in PatientSwitcherComboBox must switch StickyPatientBanner to PatientAlpha.");

            var shiftGridSv = FindRequired<ScrollViewer>(shiftGridView, "ShiftGridScrollViewer");
            shiftGridSv.Offset = default;
            PumpUi(window);

            var highAlarmKpiText = FindRequired<TextBlock>(shiftGridView, "HighAlarmKpiText");
            Button? highAlarmKpiBtn = highAlarmKpiText.FindAncestorOfType<Button>();
            Assert(highAlarmKpiBtn is not null, "HighAlarmKpiText must reside inside clickable KPI Button.");
            ClickControlCenter(window, highAlarmKpiBtn!);
            Assert(mainVm.ShiftGridVm.SelectedRiskFilter == "HighRedOnly" &&
                   mainVm.ShiftGridVm.FilteredBeds.All(b => b.IsHighAlarm),
                "Clicking High Alarms KPI box must filter ShiftGrid to HighRedOnly.");

            var totalBedsKpiText = FindRequired<TextBlock>(shiftGridView, "TotalBedsKpiText");
            Button? totalBedsKpiBtn = totalBedsKpiText.FindAncestorOfType<Button>();
            Assert(totalBedsKpiBtn is not null, "TotalBedsKpiText must reside inside clickable KPI Button.");
            CrossScreenGeometryAuditor.TryGetBoundsInWindow(totalBedsKpiBtn!, window, out Rect totalBtnRect);
            ClickControlCenter(window, totalBedsKpiBtn!);
            Assert(mainVm.ShiftGridVm.SelectedRiskFilter == "All" &&
                   mainVm.ShiftGridVm.FilteredBeds.Count == mainVm.ShiftGridVm.TotalBedCount,
                $"Clicking Active Beds KPI box must restore all beds (filter='{mainVm.ShiftGridVm.SelectedRiskFilter}', filtered={mainVm.ShiftGridVm.FilteredBeds.Count}/{mainVm.ShiftGridVm.TotalBedCount}, rect={totalBtnRect}, canExec={totalBedsKpiBtn!.Command?.CanExecute(totalBedsKpiBtn.CommandParameter)}, isEnabled={totalBedsKpiBtn.IsEffectivelyEnabled}).");
        });

        // -----------------------------------------------------------------
        // ITERATION 29: 1-Click Pending Ack Jump (ShiftGrid -> SyncMonitor -> Open Chart)
        // -----------------------------------------------------------------
        Console.WriteLine("[ITER 29/33] 1-Click Pending Ack Jump (ShiftGrid -> SyncMonitor) & Open Patient Chart...");
        CaptureStep("ITER29:PendingAckJump", () =>
        {
            mainVm.SelectWorkspaceTab(WorkspaceTab.ShiftGrid);
            mainVm.ShiftGridVm.SetRiskFilterCommand.Execute("All");
            PumpUi(window);

            var pendingAckKpiText = FindRequired<TextBlock>(shiftGridView, "PendingAckKpiText");
            Button? pendingAckKpiBtn = pendingAckKpiText.FindAncestorOfType<Button>();
            Assert(pendingAckKpiBtn is not null, "PendingAckKpiText must reside inside clickable KPI Button.");
            Assert(mainVm.ShiftGridVm.PendingHandshakeCount >= 1, "Expected at least 1 pending handshake before jump test.");

            ClickControlCenter(window, pendingAckKpiBtn!);
            Assert(mainVm.ActiveTab == WorkspaceTab.SyncMonitor &&
                   mainVm.SyncMonitorVm.SelectedConflict is not null,
                $"Clicking Pending Acks KPI must jump to SyncMonitor with SelectedConflict populated (tab={mainVm.ActiveTab}).");

            var openChartBtn = FindRequired<Button>(syncMonitorView, "OpenPatientChartButton");
            string expectedPatientId = mainVm.SyncMonitorVm.SelectedConflict!.PatientId;
            openChartBtn.BringIntoView();
            PumpUi(window);
            ClickControlCenter(window, openChartBtn);
            Assert(mainVm.ActiveTab == WorkspaceTab.PatientAnalytics &&
                   mainVm.BannerVm.SelectedPatientId == expectedPatientId,
                $"OpenPatientChartButton must navigate to PatientAnalytics for '{expectedPatientId}', got tab={mainVm.ActiveTab}, patient={mainVm.BannerVm.SelectedPatientId}.");
        });

        // -----------------------------------------------------------------
        // ITERATION 30: Live Cross-Tab Data Sync Across All 4 Tabs
        // -----------------------------------------------------------------
        Console.WriteLine("[ITER 30/33] Live Cross-Tab Data Sync (Analytics Lab -> SafetyRx; SafetyRx Order -> Banner/ShiftGrid/SyncMonitor)...");
        CaptureStep("ITER30:LiveCrossTabSync", () =>
        {
            mainVm.BannerVm.SelectPatientById(patientBetaId, notifyListeners: true);
            PumpUi(window);
            Assert(mainVm.SafetyRxVm.FormattedTitrationSummary.Contains("Awaiting", StringComparison.OrdinalIgnoreCase),
                $"PatientBeta must initially have no Hb titration, got '{mainVm.SafetyRxVm.FormattedTitrationSummary}'.");

            PressKeyCombo(window, Key.D2, RawInputModifiers.Control);
            mainVm.AnalyticsVm.SelectLabsTabCommand.Execute(null);
            PumpUi(window);
            var toggleAddLabBtn = FindRequired<Button>(analyticsView, "ToggleAddLabDrawerButton");
            var addLabDrawer = FindRequired<Border>(analyticsView, "AddLabDrawerBorder");
            if (!addLabDrawer.IsVisible)
            {
                ClickControlCenter(window, toggleAddLabBtn);
            }
            var addLabValueBox = FindRequired<TextBox>(analyticsView, "AddLabValueTextBox");
            mainVm.AnalyticsVm.SelectedAddLabCatalog =
                mainVm.AnalyticsVm.CatalogLoincItems.FirstOrDefault(c => c.LoincCode == "718-7");
            mainVm.AnalyticsVm.AddLabLoincCode = "718-7";
            mainVm.AnalyticsVm.AddLabPhase = "Pre-Dialysis";
            mainVm.AnalyticsVm.AddLabValue = "9.4";
            addLabValueBox.Focus();
            PumpUi(window);
            PressKeyCombo(window, Key.Enter, RawInputModifiers.None);
            if (addLabDrawer.IsVisible)
            {
                ClickControlCenter(window, toggleAddLabBtn);
            }

            Assert(mainVm.SafetyRxVm.FormattedTitrationSummary.Contains("AnemiaCare HD", StringComparison.Ordinal) &&
                   !mainVm.SafetyRxVm.FormattedTitrationSummary.Contains("Awaiting", StringComparison.OrdinalIgnoreCase),
                $"Saving Hb 9.4 in Analytics must live-sync SafetyRxVm AnemiaCare HD titration, got '{mainVm.SafetyRxVm.FormattedTitrationSummary}'.");

            PressKeyCombo(window, Key.D3, RawInputModifiers.Control);
            int pendingBeforeOrder = mainVm.ShiftGridVm.PendingHandshakeCount;
            mainVm.SafetyRxVm.NewDryWeightKgText = "79.8";
            mainVm.SafetyRxVm.DryWeightRationaleText = "Live cross-tab dry weight sync verification";
            mainVm.SafetyRxVm.SaveDryWeightCommand.Execute(null);
            PumpUi(window);

            var bannerDryWeight = FindRequired<TextBlock>(stickyBanner, "BannerDryWeightText");
            Assert(bannerDryWeight.Text?.Contains("79.8 kg", StringComparison.Ordinal) == true,
                $"StickyBanner must live-sync to 79.8 kg, got '{bannerDryWeight.Text}'.");
            var betaTileAfter = mainVm.ShiftGridVm.AllBedTiles.First(b => b.PatientId == patientBetaId);
            Assert(Math.Abs(betaTileAfter.TargetDryWeightKg - 79.8) < 0.01 &&
                   mainVm.ShiftGridVm.PendingHandshakeCount > pendingBeforeOrder,
                "ShiftGrid must live-sync PatientBeta TargetDryWeightKg=79.8 and PendingHandshakeCount after SafetyRx order.");
            Assert(mainVm.SyncMonitorVm.ConflictQueueItems.Any(h => h.PatientId == patientBetaId && h.DirectiveType == "DRY_WEIGHT_UPDATE"),
                "SyncMonitor ConflictQueueItems must live-sync new DRY_WEIGHT_UPDATE directive.");
        });

        // Restore PatientAlpha cleanly before viewport audits & screenshots
        mainVm.BannerVm.SelectPatientById(patientAlphaId, notifyListeners: true);
        mainVm.ShiftGridVm.SetRiskFilterCommand.Execute("All");
        mainVm.ShiftGridVm.FilterText = string.Empty;
        mainVm.SelectWorkspaceTab(WorkspaceTab.ShiftGrid);
        PumpUi(window);
    }

    public static void AuditViewportTabWave1Invariants(
        MainWindow window,
        MainWindowViewModel mainVm,
        WorkspaceTab tab,
        double width,
        double height,
        string viewportLabel)
    {
        var stickyBanner = FindRequired<StickyPatientBanner>(window, "StickyBanner");
        var shiftGridView = FindRequiredDescendant<ShiftGridView>(window);
        var analyticsView = FindRequiredDescendant<AnalyticsView>(window);
        var safetyRxView = FindRequiredDescendant<SafetyRxWorkbenchView>(window);
        var syncMonitorView = FindRequiredDescendant<SyncMonitorView>(window);

        Control activeView = tab switch
        {
            WorkspaceTab.ShiftGrid => shiftGridView,
            WorkspaceTab.PatientAnalytics => analyticsView,
            WorkspaceTab.SafetyRxWorkbench => safetyRxView,
            WorkspaceTab.SyncMonitor => syncMonitorView,
            _ => throw new ArgumentOutOfRangeException(nameof(tab))
        };

        CrossScreenGeometryAuditor.AuditNoDeveloperJargon(stickyBanner, $"{viewportLabel}:Banner");
        CrossScreenGeometryAuditor.AuditNoDeveloperJargon(activeView, $"{viewportLabel}:{tab}");
        CrossScreenGeometryAuditor.AuditIsmpTypography(activeView, $"{viewportLabel}:{tab}");

        if (tab == WorkspaceTab.ShiftGrid)
        {
            double cardWidth = mainVm.ShiftGridVm.BedCardWidth;
            if (Math.Abs(width - 1024) < 1)
            {
                Assert(cardWidth is >= 320 and <= 400,
                    $"[1024x700] Expected 2-col responsive BedCardWidth ~340-390, got {cardWidth:0.#}.");
            }
            else if (Math.Abs(width - 1366) < 1)
            {
                Assert(cardWidth is >= 290 and <= 380,
                    $"[1366x768] Expected 3-col responsive BedCardWidth ~320-360, got {cardWidth:0.#}.");
            }
            else if (Math.Abs(width - 1920) < 1)
            {
                Assert(cardWidth is >= 340 and <= 430,
                    $"[1920x1080] Expected 4-col responsive BedCardWidth ~360-420, got {cardWidth:0.#}.");
            }

            if (height <= 800)
            {
                var bedTiles = FindRequired<ItemsControl>(shiftGridView, "BedTilesItemsControl");
                Border? firstCard = bedTiles.GetVisualDescendants()
                    .OfType<Border>()
                    .FirstOrDefault(b => b.DataContext is ShiftBedTileItem);
                Assert(firstCard is not null, $"[{viewportLabel}] Expected first rendered ShiftBedTileItem Border.");
                Rect cardRect = CrossScreenGeometryAuditor.AuditAboveTheFold(
                    firstCard!,
                    window,
                    maxTop: height - 140.0,
                    contextTag: $"{viewportLabel}:ShiftGridFirstRow");
                Console.WriteLine($"    [ABOVE-FOLD {viewportLabel}] ShiftGrid Row1 Top={cardRect.Top:0.#}px (limit < {height - 140:0.#}px), CardWidth={cardWidth:0.#}px");
            }
        }
        else if (tab == WorkspaceTab.PatientAnalytics && Math.Abs(width - 1366) < 1)
        {
            var labsTabBtn = FindRequired<Button>(analyticsView, "LabsTabButton");
            var sessionsTabBtn = FindRequired<Button>(analyticsView, "SessionsTabButton");
            var labsPanel = FindRequired<StackPanel>(analyticsView, "BloodLabsTabPanel");
            var sessionsPanel = FindRequired<StackPanel>(analyticsView, "SessionsKtvTabPanel");
            var labChart = FindRequired<ClinicalTrendChartControl>(analyticsView, "LabTrendChart");
            var sessionChart = FindRequired<ClinicalTrendChartControl>(analyticsView, "SessionTrendChart");

            Assert(labsTabBtn.IsEffectivelyVisible && sessionsTabBtn.IsEffectivelyVisible,
                $"[{viewportLabel}] Patient Trends segmented tab buttons must stay visible.");

            bool startedOnLabs = mainVm.AnalyticsVm.IsLabsTabActive;
            if (!startedOnLabs)
            {
                mainVm.AnalyticsVm.SelectLabsTabCommand.Execute(null);
                PumpUi(window);
            }

            Assert(labsPanel.IsVisible && !sessionsPanel.IsVisible,
                $"[{viewportLabel}] Blood Labs tab must show BloodLabsTabPanel and hide SessionsKtvTabPanel.");
            CrossScreenGeometryAuditor.TryGetBoundsInWindow(labChart, window, out Rect labRect);
            Assert(labRect.Top < height && labRect.Height > 120.0,
                $"[{viewportLabel}] LabTrendChart must render above the fold on Tab 1 (Top={labRect.Top:0.#}, H={labRect.Height:0.#}).");

            mainVm.AnalyticsVm.SelectSessionsTabCommand.Execute(null);
            PumpUi(window);
            Assert(!labsPanel.IsVisible && sessionsPanel.IsVisible,
                $"[{viewportLabel}] Sessions & Kt/V tab must show SessionsKtvTabPanel and hide BloodLabsTabPanel.");
            CrossScreenGeometryAuditor.TryGetBoundsInWindow(sessionChart, window, out Rect sessRect);
            Assert(sessRect.Top < height && sessRect.Height > 120.0,
                $"[{viewportLabel}] SessionTrendChart must start above the fold on Tab 2 (Top={sessRect.Top:0.#} >= {height}).");

            if (startedOnLabs)
            {
                mainVm.AnalyticsVm.SelectLabsTabCommand.Execute(null);
                PumpUi(window);
            }
            Console.WriteLine($"    [ABOVE-FOLD {viewportLabel}] LabChart=({labRect.Top:0.#}..{labRect.Bottom:0.#}), SessionChart=({sessRect.Top:0.#}..{sessRect.Bottom:0.#})");
        }
        else if (tab == WorkspaceTab.SafetyRxWorkbench)
        {
            var newDwBox = FindRequired<TextBox>(safetyRxView, "NewDryWeightTextBox");
            var signRxBtn = FindRequired<Button>(safetyRxView, "SignPrescriptionButton");
            var fillDefaultsBtn = FindRequired<Button>(safetyRxView, "FillStandardDefaultsButton");
            var medExpander = FindRequired<Expander>(safetyRxView, "MedicationAnemiaExpander");
            var reasoningExpander = FindRequired<Expander>(safetyRxView, "ClinicalReasoningExpander");
            var detPane = FindRequired<Border>(safetyRxView, "DeterministicBasisPane");
            var qualPane = FindRequired<Border>(safetyRxView, "QualitativeGuidancePane");

            Assert(!medExpander.IsExpanded && !reasoningExpander.IsExpanded &&
                   !detPane.IsEffectivelyVisible && !qualPane.IsEffectivelyVisible,
                $"[{viewportLabel}] Secondary MedicationAnemiaExpander and ClinicalReasoningExpander must stay collapsed on default SafetyRx load.");
            Assert(fillDefaultsBtn.IsEffectivelyVisible,
                $"[{viewportLabel}] Opt-in FillStandardDefaultsButton must remain visible on PrescriptionOrderCard.");

            if (Math.Abs(width - 1366) < 1)
            {
                Rect dwRect = CrossScreenGeometryAuditor.AuditAboveTheFold(
                    newDwBox, window, maxTop: 550.0, contextTag: $"{viewportLabel}:SafetyRx.NewDryWeight", maxBottom: height);
                double maxSignRxBottom = height >= 820.0 ? 790.0 : height;
                Rect rxRect = CrossScreenGeometryAuditor.AuditAboveTheFold(
                    signRxBtn, window, maxTop: 720.0, contextTag: $"{viewportLabel}:SafetyRx.SignRxButton", maxBottom: maxSignRxBottom);
                Console.WriteLine($"    [ABOVE-FOLD {viewportLabel}] SafetyRx DryWeight=({dwRect.Top:0.#}..{dwRect.Bottom:0.#}), SignRx=({rxRect.Top:0.#}..{rxRect.Bottom:0.#}, limit<={maxSignRxBottom:0.#})");
            }

            reasoningExpander.IsExpanded = true;
            PumpUi(window);
            Assert(detPane.IsEffectivelyVisible && qualPane.IsEffectivelyVisible,
                $"[{viewportLabel}] Criterion 4 panes must become visible when ClinicalReasoningExpander expands.");
            CrossScreenGeometryAuditor.AuditVerticalOrder(newDwBox, detPane, window, $"{viewportLabel}:SafetyRx.DryWeightBeforeC4");
            CrossScreenGeometryAuditor.AuditVerticalOrder(signRxBtn, detPane, window, $"{viewportLabel}:SafetyRx.RxBeforeC4");
            reasoningExpander.IsExpanded = false;
            PumpUi(window);
        }
        else if (tab == WorkspaceTab.SyncMonitor)
        {
            var queueBox = FindRequired<ListBox>(syncMonitorView, "ConflictQueueListBox");
            var conflictFormPanel = FindRequired<StackPanel>(syncMonitorView, "ConflictResolutionFormPanel");
            var calmStatusCard = FindRequired<Border>(syncMonitorView, "CalmOrderStatusCard");
            var resolveBtn = FindRequired<Button>(syncMonitorView, "ResolveConflictButton");
            var staffIdBox = FindRequired<TextBox>(syncMonitorView, "ClinicianStaffIdTextBox");
            var techExpander = FindRequired<Expander>(syncMonitorView, "TechnicalSyncExpander");
            var hostBadge = FindRequired<Border>(syncMonitorView, "SyncHostStatusBadge");

            Assert(conflictFormPanel.IsVisible == mainVm.SyncMonitorVm.IsConflictResolutionVisible &&
                   calmStatusCard.IsVisible == !mainVm.SyncMonitorVm.IsConflictResolutionVisible,
                $"[{viewportLabel}] SyncMonitor ConflictResolutionFormPanel and CalmOrderStatusCard must be mutually exclusive.");
            Assert(!techExpander.IsExpanded && !hostBadge.IsEffectivelyVisible,
                $"[{viewportLabel}] TechnicalSyncExpander must start collapsed on SyncMonitor.");

            if (!mainVm.SyncMonitorVm.HasNoQueueItems)
            {
                if (Math.Abs(width - 1366) < 1 || Math.Abs(width - 1024) < 1)
                {
                    Rect qRect = CrossScreenGeometryAuditor.AuditAboveTheFold(
                        queueBox, window, maxTop: 380.0, contextTag: $"{viewportLabel}:SyncMonitor.ConflictQueue", maxBottom: height);
                    Console.WriteLine($"    [ABOVE-FOLD {viewportLabel}] SyncMonitor Queue=({qRect.Top:0.#}..{qRect.Bottom:0.#})");
                }

                techExpander.IsExpanded = true;
                PumpUi(window);
                Assert(hostBadge.IsEffectivelyVisible, $"[{viewportLabel}] Expanding TechnicalSyncExpander must reveal SyncHostStatusBadge.");
                CrossScreenGeometryAuditor.AuditVerticalOrder(queueBox, hostBadge, window, $"{viewportLabel}:SyncMonitor.QueueBeforeHost");
                if (mainVm.SyncMonitorVm.IsConflictResolutionVisible)
                {
                    CrossScreenGeometryAuditor.AuditVerticalOrder(resolveBtn, hostBadge, window, $"{viewportLabel}:SyncMonitor.ResolveBeforeHost");
                }
                techExpander.IsExpanded = false;
                PumpUi(window);
            }

            if (mainVm.SyncMonitorVm.IsConflictResolutionVisible)
            {
                if (Math.Abs(width - 1366) < 1)
                {
                    CrossScreenGeometryAuditor.AuditAboveTheFold(
                        resolveBtn, window, maxTop: 720.0, contextTag: $"{viewportLabel}:SyncMonitor.ResolveBtn", maxBottom: height);
                }
                else if (Math.Abs(width - 1024) < 1)
                {
                    CrossScreenGeometryAuditor.AuditAboveTheFold(
                        staffIdBox, window, maxTop: 650.0, contextTag: $"{viewportLabel}:SyncMonitor.ResolutionForm");
                }
            }
            else if (Math.Abs(width - 1366) < 1)
            {
                CrossScreenGeometryAuditor.AuditAboveTheFold(
                    calmStatusCard, window, maxTop: 680.0, contextTag: $"{viewportLabel}:SyncMonitor.CalmStatusCard", maxBottom: height);
            }
        }
    }

    public static string RunInteractiveChartAndSameDayChecks(
        MainWindow window,
        AnalyticsViewModel analyticsVm,
        AnalyticsView analyticsView,
        string screenshotDirectory)
    {
        Console.WriteLine("[ITER 31/33] Same-Day X-Axis Disambiguation, Interactive Chart Tooltip & Legend Toggle...");
        analyticsVm.SelectLabsTabCommand.Execute(null);
        analyticsVm.IsAddLabDrawerOpen = false;
        PumpUi(window);

        var labChart = FindRequired<ClinicalTrendChartControl>(analyticsView, "LabTrendChart");
        var sessionChart = FindRequired<ClinicalTrendChartControl>(analyticsView, "SessionTrendChart");

        Assert(labChart.SeriesPoints is not null && labChart.SeriesPoints.Count >= 2,
            "Expected >= 2 same-day Potassium points for disambiguation test.");

        var p0 = labChart.SeriesPoints![0];
        var p1 = labChart.SeriesPoints[^1];
        Assert(!string.Equals(p0.Label, p1.Label, StringComparison.Ordinal),
            $"Same-day lab X-axis labels must be disambiguated, found identical '{p0.Label}' and '{p1.Label}'.");
        Assert(!string.IsNullOrWhiteSpace(p0.FullTimestampText) && !string.IsNullOrWhiteSpace(p1.FullTimestampText),
            "Same-day ChartPointItems must populate FullTimestampText for tooltip display.");

        // Ensure LabTrendChart is rendered in viewport so PlotBoundsRect is valid
        labChart.BringIntoView();
        PumpUi(window);
        using (var _ = window.CaptureRenderedFrame())
        {
        }

        Assert(labChart.PlotBoundsRect.Width > 40 && labChart.PlotBoundsRect.Height > 40,
            $"LabTrendChart PlotBoundsRect invalid: {labChart.PlotBoundsRect}.");

        // Hover over rightmost panic point (6.5 mmol/L)
        Point localHover = new(labChart.PlotBoundsRect.Right - 10.0, labChart.PlotBoundsRect.Center.Y);
        Point? winHover = labChart.TranslatePoint(localHover, window);
        Assert(winHover.HasValue, "Could not map LabTrendChart hover point to Window coordinates.");

        window.MouseMove(winHover!.Value, RawInputModifiers.None);
        PumpUi(window);
        string tooltipShotPath = System.IO.Path.Combine(screenshotDirectory, "02c_patient_analytics_tooltip_1366x768.png");
        using (var tooltipFrame = window.CaptureRenderedFrame())
        {
            Assert(tooltipFrame is not null && tooltipFrame.PixelSize.Width > 0,
                "CaptureRenderedFrame returned empty frame for interactive tooltip.");
            tooltipFrame!.Save(tooltipShotPath);
        }

        Assert(labChart.IsTooltipVisible &&
               labChart.HoveredSlotIndex == labChart.SeriesPoints.Count - 1 &&
               labChart.LastTooltipSummary?.Contains("6.5", StringComparison.Ordinal) == true,
            $"Hovering panic Potassium point must show tooltip with 6.5 (visible={labChart.IsTooltipVisible}, slot={labChart.HoveredSlotIndex}, summary='{labChart.LastTooltipSummary}').");

        // Move pointer away -> tooltip clears
        window.MouseMove(new Point(16, 16), RawInputModifiers.None);
        PumpUi(window);
        Assert(!labChart.IsTooltipVisible, "Moving pointer off LabTrendChart must dismiss tooltip.");

        // Switch to Tab 2 (Sessions & Kt/V) so SessionTrendChart is visible, then toggle clickable legend pills
        analyticsVm.SelectSessionsTabCommand.Execute(null);
        PumpUi(window);
        sessionChart.BringIntoView();
        PumpUi(window);
        using (var _ = window.CaptureRenderedFrame())
        {
        }

        Assert(sessionChart.PrimaryLegendRect.Width > 10 && sessionChart.SecondaryLegendRect.Width > 10,
            "SessionTrendChart must compute PrimaryLegendRect and SecondaryLegendRect for Pre-SBP / Nadir-SBP.");

        Point? secLegendWin = sessionChart.TranslatePoint(sessionChart.SecondaryLegendRect.Center, window);
        Point? priLegendWin = sessionChart.TranslatePoint(sessionChart.PrimaryLegendRect.Center, window);
        Assert(secLegendWin.HasValue && priLegendWin.HasValue, "Could not map legend rects to Window coordinates.");

        Assert(sessionChart.IsPrimarySeriesVisible && sessionChart.IsSecondarySeriesVisible,
            "Both chart series must start visible.");

        ClickWindowPoint(window, secLegendWin!.Value);
        Assert(!sessionChart.IsSecondarySeriesVisible && sessionChart.IsPrimarySeriesVisible,
            "Clicking Secondary legend pill must toggle IsSecondarySeriesVisible off.");

        ClickWindowPoint(window, secLegendWin.Value);
        Assert(sessionChart.IsSecondarySeriesVisible,
            "Clicking Secondary legend pill again must restore IsSecondarySeriesVisible.");

        ClickWindowPoint(window, priLegendWin!.Value);
        Assert(!sessionChart.IsPrimarySeriesVisible,
            "Clicking Primary legend pill must toggle IsPrimarySeriesVisible off.");

        ClickWindowPoint(window, priLegendWin.Value);
        Assert(sessionChart.IsPrimarySeriesVisible,
            "Clicking Primary legend pill again must restore IsPrimarySeriesVisible.");

        analyticsVm.SelectLabsTabCommand.Execute(null);
        PumpUi(window);

        Console.WriteLine($"  -> PASS: Same-day labels ('{p0.Label}' vs '{p1.Label}'), hover tooltip ('{labChart.LastTooltipSummary ?? "cleared"}'), and legend toggles verified.");
        return tooltipShotPath;
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
        ClickWindowPoint(window, center);
    }

    private static void ClickWindowPoint(Window window, Point point)
    {
        window.MouseMove(point, RawInputModifiers.None);
        PumpUi(window);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        PumpUi(window);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        PumpUi(window);
    }

    private static void PressKeyCombo(Window window, Key key, RawInputModifiers modifiers)
    {
        PhysicalKey physical = key switch
        {
            Key.D1 => PhysicalKey.Digit1,
            Key.D2 => PhysicalKey.Digit2,
            Key.D3 => PhysicalKey.Digit3,
            Key.D4 => PhysicalKey.Digit4,
            Key.R => PhysicalKey.R,
            Key.F5 => PhysicalKey.F5,
            Key.Escape => PhysicalKey.Escape,
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
