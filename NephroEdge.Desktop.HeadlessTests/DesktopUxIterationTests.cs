using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NephroEdge.Desktop.Bootstrap;
using NephroEdge.Desktop.Controls;
using NephroEdge.Desktop.Infrastructure;
using NephroEdge.Desktop.ViewModels;
using NephroEdge.Desktop.Views;
using Persistence_Unit.Drivers;
using Persistence_Unit.Entities;
using Persistence_Unit.Interfaces;
using Services_Unit.Core.Formatting;
using Services_Unit.Core.Interfaces;
using Services_Unit.RiskPrediction.Contracts;
using Services_Unit.SyncBridge;

namespace NephroEdge.Desktop.HeadlessTests;

/// <summary>
/// Executes 24 automated Avalonia Headless cross-screen MedTech UX, geometry, ISMP typography,
/// IEC 60601-1-8 alarm, FDA Criterion 4, SCD-2, :5050 sync conflict, and screenshot iterations.
/// </summary>
public static class DesktopUxIterationTests
{
    private static readonly string ScreenshotDirectory =
        Environment.GetEnvironmentVariable("NEPHROEDGE_SCREENSHOT_DIR") is { Length: > 0 } envDir
            ? envDir
            : Path.Combine(Path.GetTempPath(), "nephroedge_desktop_screens");

    private const string ShiftDate = "2026-11-10";
    private const int ShiftNum = 1;

    private const string PatientAlphaId = "PT-UX-ALPHA";
    private const string PatientAlphaMrn = "MRN-UX-ALPHA";
    private const string PatientBetaId = "PT-UX-BETA";
    private const string PatientBetaMrn = "MRN-UX-BETA";
    private const string PatientNomId = "PT-UX-NOM";

    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static void RunAllIterations()
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine(" NEPHROEDGE PART 2 — 24-ITERATION AVALONIA HEADLESS MEDTECH UX & GEOMETRY AUDIT");
        Console.WriteLine("================================================================================");

        string tempDir = Path.Combine(
            Path.GetTempPath(),
            "NephroEdge_HeadlessUx_" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(ScreenshotDirectory);

        string mendeleyZip = ResolveMendeleyZip();
        VerifyBootstrapAutoHealCorruptedDuplicateDatabase(mendeleyZip);
        int freePort = GetFreeTcpPort();
        var capturedScreenshots = new List<string>();

        try
        {
            using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

            session.Dispatch(() =>
            {
                using var bootstrap = new ClinicWorkspaceBootstrap(
                    workspaceDirectory: tempDir,
                    startSyncHost: true,
                    syncHostOptions: new LocalSyncHostOptions(
                        Port: freePort,
                        BindMode: LocalSyncBindMode.LocalhostAdbOnly,
                        DefaultSessionDate: ShiftDate,
                        DefaultShiftNumber: ShiftNum),
                    mendeleyZipPathOverride: mendeleyZip);

                Assert(bootstrap.InitialSeedReport is not null && bootstrap.InitialSeedReport.ValidSessionsInserted > 0,
                    "Mendeley cohort must seed cleanly when workspace database starts empty.");
                Assert(bootstrap.SyncHost is not null && bootstrap.SyncHost.IsListening,
                    $"Embedded LocalSyncHttpHost must start on ephemeral port {freePort}.");

                SeedDeterministicUxPatients(bootstrap.ServiceProvider);
                string longitudinalMendeleyPatientId = FindLongitudinalMendeleyPatientId(bootstrap.ServiceProvider);

                using var mainVm = new MainWindowViewModel(
                    bootstrap.ScopeFactory,
                    () => bootstrap.SyncHost,
                    bootstrap.RestartSyncHostAsync);

                var window = new MainWindow
                {
                    DataContext = mainVm,
                    Width = 1366,
                    Height = 768
                };

                try
                {
                    window.Show();
                    PumpUi(window);

                    // Lookup locked views & controls
                    var stickyBanner = FindRequired<StickyPatientBanner>(window, "StickyBanner");
                    var bannerNameText = FindRequired<TextBlock>(stickyBanner, "BannerPatientNameText");
                    var bannerMrnText = FindRequired<TextBlock>(stickyBanner, "BannerMrnText");
                    var bannerIdText = FindRequired<TextBlock>(stickyBanner, "BannerPatientIdText");
                    var bannerIsoHigh = FindRequired<Border>(stickyBanner, "BannerIsolationHighBorder");
                    var bannerIsoStd = FindRequired<Border>(stickyBanner, "BannerIsolationStandardBorder");
                    var bannerDryWeight = FindRequired<TextBlock>(stickyBanner, "BannerDryWeightText");

                    var shiftGridView = FindRequiredDescendant<ShiftGridView>(window);
                    var analyticsView = FindRequiredDescendant<AnalyticsView>(window);
                    var safetyRxView = FindRequiredDescendant<SafetyRxWorkbenchView>(window);
                    var syncMonitorView = FindRequiredDescendant<SyncMonitorView>(window);
                    var shellSyncCard = FindRequired<Border>(window, "ShellSyncIndicatorCard");

                    // -----------------------------------------------------------------
                    // ITERATIONS 1-3: IPSG.1 Sticky 2-ID Banner & Zero-Stale Switching
                    // -----------------------------------------------------------------
                    Console.WriteLine("[ITER 1/24] IPSG.1 Sticky Banner Dual Identifiers & HBV+ Isolation Badge...");
                    mainVm.BannerVm.SelectPatientById(PatientAlphaId, notifyListeners: true);
                    PumpUi(window);

                    CrossScreenGeometryAuditor.AssertNotInsideScrollViewer(stickyBanner, "StickyBanner");
                    Assert(bannerNameText.Text?.Contains("Amira", StringComparison.Ordinal) == true,
                        $"Expected Patient Alpha name in banner, got '{bannerNameText.Text}'.");
                    Assert(bannerMrnText.Text?.Contains(PatientAlphaMrn, StringComparison.Ordinal) == true,
                        $"Expected Patient Alpha MRN '{PatientAlphaMrn}' in banner, got '{bannerMrnText.Text}'.");
                    Assert(bannerIdText.Text?.Contains(PatientAlphaId, StringComparison.Ordinal) == true,
                        $"Expected Patient Alpha ID '{PatientAlphaId}' in banner, got '{bannerIdText.Text}'.");
                    Assert(bannerIsoHigh.IsVisible && !bannerIsoStd.IsVisible,
                        "Patient Alpha (IsolationRequired=1) must display HBV+ high-contrast isolation badge.");
                    Console.WriteLine("  -> PASS: Sticky Banner shows FullName + MRN + PatientId + HBV+ ISOLATION outside ScrollViewer.");

                    Console.WriteLine("[ITER 2/24] Sticky Banner Persistence Across All 4 Workspace Tabs...");
                    foreach (WorkspaceTab tab in Enum.GetValues<WorkspaceTab>())
                    {
                        InvokeCommandForTab(mainVm, tab);
                        PumpUi(window);
                        Assert(stickyBanner.IsEffectivelyVisible && stickyBanner.Bounds.Height > 40.0,
                            $"StickyBanner must stay visible across tab {tab}.");
                        Assert(bannerMrnText.Text?.Contains(PatientAlphaMrn, StringComparison.Ordinal) == true,
                            $"StickyBanner MRN must stay pinned on tab {tab}.");
                        Assert(bannerIsoHigh.IsVisible,
                            $"HBV+ Isolation badge must stay pinned on tab {tab}.");
                    }
                    Console.WriteLine("  -> PASS: IPSG.1 2-ID Sticky Banner remained pinned across all 4 tabs.");

                    Console.WriteLine("[ITER 3/24] Patient Switcher Context Switch & Zero Stale Fields...");
                    InvokeCommand(mainVm.NavigateSafetyRxCommand);
                    PumpUi(window);
                    Assert(mainVm.SafetyRxVm.PatientHeaderLabel.Contains(PatientAlphaMrn, StringComparison.Ordinal),
                        "SafetyRx header should reflect Patient Alpha before switch.");

                    var betaSummary = mainVm.BannerVm.AllPatients.First(p => p.PatientId == PatientBetaId);
                    mainVm.BannerVm.SelectedPatient = betaSummary;
                    PumpUi(window);

                    Assert(bannerNameText.Text?.Contains("Karim", StringComparison.Ordinal) == true,
                        $"Expected Patient Beta name after switch, got '{bannerNameText.Text}'.");
                    Assert(bannerMrnText.Text?.Contains(PatientBetaMrn, StringComparison.Ordinal) == true,
                        $"Expected Patient Beta MRN after switch, got '{bannerMrnText.Text}'.");
                    Assert(!bannerIsoHigh.IsVisible && bannerIsoStd.IsVisible,
                        "Patient Beta (IsolationRequired=0) must clear HBV+ isolation badge.");
                    Assert(mainVm.SafetyRxVm.PatientHeaderLabel.Contains(PatientBetaMrn, StringComparison.Ordinal) &&
                           !mainVm.SafetyRxVm.PatientHeaderLabel.Contains(PatientAlphaMrn, StringComparison.Ordinal),
                        "SafetyRx context must switch cleanly to Patient Beta with zero stale Alpha text.");
                    Assert(mainVm.AnalyticsVm.PatientHeaderLabel.Contains(PatientBetaMrn, StringComparison.Ordinal) &&
                           !mainVm.AnalyticsVm.PatientHeaderLabel.Contains(PatientAlphaMrn, StringComparison.Ordinal),
                        "Analytics context must switch cleanly to Patient Beta with zero stale Alpha text.");
                    Console.WriteLine("  -> PASS: Patient switcher purged all stale Patient Alpha state across Banner, Analytics, and SafetyRx.");

                    // -----------------------------------------------------------------
                    // ITERATIONS 4-6: IEC 60601-1-8 Multi-Modal Alarms & Cadence
                    // -----------------------------------------------------------------
                    Console.WriteLine("[ITER 4/24] IEC 60601-1-8 HighRed Alarm (SBP=84) + Shift Filters...");
                    InvokeCommand(mainVm.NavigateShiftGridCommand);
                    PumpUi(window);

                    var redBed = mainVm.ShiftGridVm.FilteredBeds.FirstOrDefault(b => b.PatientId == PatientAlphaId);
                    Assert(redBed is not null, "Expected Patient Alpha HighRed bed tile on ShiftGrid.");
                    Assert(redBed!.EffectiveAlarmPriority == AlarmPriorityTier.HighRed,
                        $"Expected HighRed alarm tier for SBP=84, got {redBed.EffectiveAlarmPriority}.");
                    Assert(redBed.AlarmBadgeText == "▲ Low BP",
                        $"Expected '▲ Low BP', got '{redBed.AlarmBadgeText}'.");
                    Assert(redBed.PrimaryActionText == "Review Alert →",
                        $"Expected 'Review Alert →' on HighRed bed, got '{redBed.PrimaryActionText}'.");

                    InvokeCommand(mainVm.ShiftGridVm.SetRiskFilterCommand, "HighRedOnly");
                    PumpUi(window);
                    Assert(mainVm.ShiftGridVm.FilteredBeds.All(b => b.IsHighAlarm) &&
                           mainVm.ShiftGridVm.FilteredBeds.Any(b => b.PatientId == PatientAlphaId),
                        "HighRedOnly filter must keep HighRed beds and exclude Nominal beds.");

                    InvokeCommand(mainVm.ShiftGridVm.SetRiskFilterCommand, "IsolatedOnly");
                    PumpUi(window);
                    Assert(mainVm.ShiftGridVm.FilteredBeds.All(b => b.IsIsolationRequired) &&
                           mainVm.ShiftGridVm.FilteredBeds.Any(b => b.PatientId == PatientAlphaId),
                        "IsolatedOnly filter must show only HBV+ isolated beds.");

                    InvokeCommand(mainVm.ShiftGridVm.SetRiskFilterCommand, "All");
                    PumpUi(window);
                    Console.WriteLine($"  -> PASS: ShiftGrid rendered {mainVm.ShiftGridVm.TotalBedCount} beds (HighRed={mainVm.ShiftGridVm.HighAlarmCount}, Med={mainVm.ShiftGridVm.MediumWarningCount}).");

                    Console.WriteLine("[ITER 5/24] IEC 60601-1-8 MediumYellow Warning & Nominal Stable Tiles...");
                    var medBed = mainVm.ShiftGridVm.FilteredBeds.FirstOrDefault(b => b.PatientId == PatientBetaId);
                    var nomBed = mainVm.ShiftGridVm.FilteredBeds.FirstOrDefault(b => b.PatientId == PatientNomId);
                    Assert(medBed is not null && medBed.EffectiveAlarmPriority == AlarmPriorityTier.MediumYellow && medBed.AlarmBadgeText == "◆ Warning",
                        $"Expected MediumYellow '◆ Warning' on Patient Beta bed, got {medBed?.EffectiveAlarmPriority} / '{medBed?.AlarmBadgeText}'.");
                    Assert(medBed!.PrimaryActionText == "Review Alert →",
                        $"Expected 'Review Alert →' on MediumYellow bed, got '{medBed.PrimaryActionText}'.");
                    Assert(nomBed is not null && nomBed.EffectiveAlarmPriority == AlarmPriorityTier.None && nomBed.AlarmBadgeText == "● Stable",
                        $"Expected Nominal '● Stable' on Patient Nom bed, got {nomBed?.EffectiveAlarmPriority} / '{nomBed?.AlarmBadgeText}'.");
                    Assert(nomBed!.PrimaryActionText == "Open Chart →",
                        $"Expected 'Open Chart →' on Nominal bed, got '{nomBed.PrimaryActionText}'.");
                    Console.WriteLine("  -> PASS: Multi-modal High (▲ Low BP), Medium (◆ Warning), and Nominal (● Stable) tiles verified.");

                    Console.WriteLine("[ITER 6/24] IEC 60601-1-8 Flash Cadence & Steady Cyan Technical Sync Guardrail...");
                    mainVm.ShiftGridVm.ApplyFlashCadence(highFlashOn: true, mediumFlashOn: true);
                    IBrush highOnBrush = redBed.AlarmBorderBrush;
                    IBrush medOnBrush = medBed!.AlarmBorderBrush;
                    IBrush nomSteadyBrush = nomBed!.AlarmBorderBrush;

                    mainVm.ShiftGridVm.ApplyFlashCadence(highFlashOn: false, mediumFlashOn: false);
                    IBrush highOffBrush = redBed.AlarmBorderBrush;
                    IBrush medOffBrush = medBed.AlarmBorderBrush;
                    IBrush nomAfterBrush = nomBed.AlarmBorderBrush;

                    Assert(!Equals(highOnBrush, highOffBrush), "HighRed bed border brush must pulse between highFlashOn true/false.");
                    Assert(!Equals(medOnBrush, medOffBrush), "MediumYellow bed border brush must pulse between mediumFlashOn true/false.");
                    Assert(Equals(nomSteadyBrush, nomAfterBrush), "Nominal bed border brush must stay steady.");
                    mainVm.ShiftGridVm.ApplyFlashCadence(highFlashOn: true, mediumFlashOn: true);

                    var shiftSyncBtn = FindRequired<Button>(shiftGridView, "ShiftSyncNoticeBanner");
                    CrossScreenGeometryAuditor.AuditNoRedOnTechnicalSync(shiftSyncBtn, "ShiftGrid.ShiftSyncNoticeBanner");
                    CrossScreenGeometryAuditor.AuditNoRedOnTechnicalSync(shellSyncCard, "Shell.ShellSyncIndicatorCard");
                    Console.WriteLine("  -> PASS: Flash cadence pulses High/Medium alarms; technical sync controls contain zero Red brushes.");

                    // -----------------------------------------------------------------
                    // ITERATIONS 7-10: Analytics Trends, Lab Entry & Daugirdas II
                    // -----------------------------------------------------------------
                    Console.WriteLine("[ITER 7/24] Analytics Empty Lab State Graceful Render...");
                    mainVm.BannerVm.SelectPatientById(PatientBetaId, notifyListeners: true);
                    InvokeCommand(mainVm.NavigateAnalyticsCommand);
                    PumpUi(window);

                    var labTrendChart = FindRequired<ClinicalTrendChartControl>(analyticsView, "LabTrendChart");
                    var sessionTrendChart = FindRequired<ClinicalTrendChartControl>(analyticsView, "SessionTrendChart");
                    Assert(mainVm.AnalyticsVm.LabTrends.Count == 0, "Patient Beta must start with zero recorded labs.");
                    Assert(!string.IsNullOrWhiteSpace(mainVm.AnalyticsVm.LabEmptyStateMessage), "Empty state message must be populated.");
                    Assert(labTrendChart.Bounds.Width > 200 && labTrendChart.Bounds.Height > 120,
                        $"LabTrendChart bounds invalid in empty state: {labTrendChart.Bounds}.");
                    Console.WriteLine($"  -> PASS: Empty lab state renders cleanly ({labTrendChart.Bounds.Width:0}x{labTrendChart.Bounds.Height:0}).");

                    Console.WriteLine("[ITER 8/24] Interactive Manual Lab Entry (2823-3 Potassium 5.2 -> 6.5 Panic)...");
                    mainVm.AnalyticsVm.AddLabLoincCode = "2823-3";
                    mainVm.AnalyticsVm.AddLabPhase = "Pre-Dialysis";
                    mainVm.AnalyticsVm.AddLabValue = "5.2";
                    InvokeCommand(mainVm.AnalyticsVm.SaveLabResultCommand);
                    PumpUi(window);

                    mainVm.AnalyticsVm.AddLabLoincCode = "2823-3";
                    mainVm.AnalyticsVm.AddLabValue = "6.5";
                    InvokeCommand(mainVm.AnalyticsVm.SaveLabResultCommand);
                    PumpUi(window);

                    Assert(mainVm.AnalyticsVm.LabTrends.Count == 1, $"Expected 1 lab trend (2823-3), got {mainVm.AnalyticsVm.LabTrends.Count}.");
                    Assert(mainVm.AnalyticsVm.SelectedLabTrend is not null &&
                           mainVm.AnalyticsVm.SelectedLabTrend.LoincCode == "2823-3" &&
                           mainVm.AnalyticsVm.SelectedLabTrend.HasPanicPoint,
                        "LOINC 2823-3 at 6.5 mmol/L must trigger critical panic detection.");
                    Assert(labTrendChart.SeriesPoints is not null &&
                           labTrendChart.SeriesPoints.Count == 2 &&
                           labTrendChart.SeriesPoints[1].IsCriticalPanic,
                        "LabTrendChart must plot 2 points with second point flagged IsCriticalPanic=true.");
                    Assert(labTrendChart.Bounds.Width > 200 && labTrendChart.Bounds.Height > 120,
                        $"LabTrendChart bounds must exceed 200x120, got {labTrendChart.Bounds}.");
                    Console.WriteLine("  -> PASS: Lab entry persisted 2823-3 Potassium trajectory and flagged 6.5 mmol/L panic.");

                    Console.WriteLine("[ITER 9/24] Mendeley Longitudinal Session Time-Series Toggle (5 / 10 / 20)...");
                    var labsTabButton = FindRequired<Button>(analyticsView, "LabsTabButton");
                    var sessionsTabButton = FindRequired<Button>(analyticsView, "SessionsTabButton");
                    var bloodLabsTabPanel = FindRequired<StackPanel>(analyticsView, "BloodLabsTabPanel");
                    var sessionsKtvTabPanel = FindRequired<StackPanel>(analyticsView, "SessionsKtvTabPanel");
                    var addLabDrawerBorder = FindRequired<Border>(analyticsView, "AddLabDrawerBorder");
                    var window5Button = FindRequired<Button>(analyticsView, "Window5Button");
                    var window10Button = FindRequired<Button>(analyticsView, "Window10Button");
                    var window20Button = FindRequired<Button>(analyticsView, "Window20Button");

                    Assert(mainVm.AnalyticsVm.IsLabsTabActive && !mainVm.AnalyticsVm.IsSessionsTabActive &&
                           bloodLabsTabPanel.IsVisible && !sessionsKtvTabPanel.IsVisible && !addLabDrawerBorder.IsVisible,
                        "AnalyticsView must default to Tab 1 (Blood Labs) with Sessions tab and AddLabDrawerBorder collapsed.");

                    mainVm.BannerVm.SelectPatientById(longitudinalMendeleyPatientId, notifyListeners: true);
                    InvokeCommand(mainVm.AnalyticsVm.SelectSessionsTabCommand);
                    PumpUi(window);
                    Assert(!mainVm.AnalyticsVm.IsLabsTabActive && mainVm.AnalyticsVm.IsSessionsTabActive &&
                           !bloodLabsTabPanel.IsVisible && sessionsKtvTabPanel.IsVisible &&
                           labsTabButton.Classes.Contains("active") == false && sessionsTabButton.Classes.Contains("active"),
                        "SelectSessionsTabCommand must activate Tab 2 (Sessions & Kt/V) and update segmented tab classes.");

                    InvokeCommand(mainVm.AnalyticsVm.SetSessionWindowCommand, "5");
                    PumpUi(window);
                    int countAt5 = mainVm.AnalyticsVm.SessionRows.Count;
                    Assert(countAt5 > 0 && countAt5 <= 5, $"Expected 1..5 rows for window=5, got {countAt5}.");
                    Assert(mainVm.AnalyticsVm.IsWindow5Selected && !mainVm.AnalyticsVm.IsWindow10Selected && !mainVm.AnalyticsVm.IsWindow20Selected &&
                           window5Button.Classes.Contains("active") && !window10Button.Classes.Contains("active") && !window20Button.Classes.Contains("active"),
                        "Window5Button active highlight must match SelectedSessionWindowCount == 5.");

                    InvokeCommand(mainVm.AnalyticsVm.SetSessionWindowCommand, "10");
                    PumpUi(window);
                    int countAt10 = mainVm.AnalyticsVm.SessionRows.Count;
                    Assert(countAt10 >= countAt5 && countAt10 <= 10, $"Expected >= {countAt5} and <= 10 rows for window=10, got {countAt10}.");
                    Assert(!mainVm.AnalyticsVm.IsWindow5Selected && mainVm.AnalyticsVm.IsWindow10Selected && !mainVm.AnalyticsVm.IsWindow20Selected &&
                           !window5Button.Classes.Contains("active") && window10Button.Classes.Contains("active") && !window20Button.Classes.Contains("active"),
                        "Window10Button active highlight must match SelectedSessionWindowCount == 10.");

                    InvokeCommand(mainVm.AnalyticsVm.SetSessionWindowCommand, "20");
                    PumpUi(window);
                    int countAt20 = mainVm.AnalyticsVm.SessionRows.Count;
                    Assert(countAt20 >= countAt10, $"Expected >= {countAt10} rows for window=20, got {countAt20}.");
                    Assert(!mainVm.AnalyticsVm.IsWindow5Selected && !mainVm.AnalyticsVm.IsWindow10Selected && mainVm.AnalyticsVm.IsWindow20Selected &&
                           !window5Button.Classes.Contains("active") && !window10Button.Classes.Contains("active") && window20Button.Classes.Contains("active"),
                        "Window20Button active highlight must match SelectedSessionWindowCount == 20.");
                    Assert(sessionTrendChart.CriticalThresholdLine == 90.0,
                        "SessionTrendChart must enforce Flythe 90 mmHg critical threshold line.");
                    Assert(sessionTrendChart.Bounds.Width > 200 && sessionTrendChart.Bounds.Height > 120,
                        $"SessionTrendChart bounds invalid: {sessionTrendChart.Bounds}.");
                    Console.WriteLine($"  -> PASS: Mendeley longitudinal patient '{longitudinalMendeleyPatientId}' session windows 5/10/20 -> {countAt5}/{countAt10}/{countAt20} rows + synced pill highlights.");

                    Console.WriteLine("[ITER 10/24] Daugirdas II spKt/V Boundaries (Adequate 1.429, Inadequate 0.933, Safe Domain Rejections)...");
                    var ktvExpander = FindRequired<Expander>(analyticsView, "KtvCalculatorExpander");
                    var dryWeightExpander = FindRequired<Expander>(analyticsView, "DryWeightLogExpander");
                    var dryWeightTimeline = FindRequired<ItemsControl>(analyticsView, "DryWeightTimelineList");
                    Assert(!ktvExpander.IsExpanded && !dryWeightExpander.IsExpanded,
                        "KtvCalculatorExpander and DryWeightLogExpander must start collapsed on Tab 2.");

                    InvokeCommand(mainVm.AnalyticsVm.ToggleKtvDrawerCommand);
                    PumpUi(window);
                    Assert(ktvExpander.IsExpanded && FindRequired<TextBox>(analyticsView, "KtVPreBunTextBox").IsEffectivelyVisible,
                        "ToggleKtvDrawerCommand must expand KtvCalculatorExpander cleanly.");

                    SetKtVInputs(mainVm.AnalyticsVm, "65", "19", "4", "2", "72");
                    InvokeCommand(mainVm.AnalyticsVm.ComputeKtVCommand);
                    PumpUi(window);
                    Assert(mainVm.AnalyticsVm.KtVResult is { IsValid: true, IsAdequateKdoqiTarget: true } &&
                           Math.Abs(mainVm.AnalyticsVm.KtVResult.SpKtV - 1.429) < 0.01,
                        $"Expected valid adequate spKt/V ~1.429, got {mainVm.AnalyticsVm.KtVResult?.SpKtV}.");

                    SetKtVInputs(mainVm.AnalyticsVm, "65", "28", "3", "1", "75");
                    InvokeCommand(mainVm.AnalyticsVm.ComputeKtVCommand);
                    PumpUi(window);
                    Assert(mainVm.AnalyticsVm.KtVResult is { IsValid: true, IsAdequateKdoqiTarget: false } &&
                           mainVm.AnalyticsVm.KtVIsInadequateWarning &&
                           Math.Abs(mainVm.AnalyticsVm.KtVResult.SpKtV - 0.933) < 0.01,
                        $"Expected inadequate spKt/V ~0.933 flagged < 1.2, got {mainVm.AnalyticsVm.KtVResult?.SpKtV}.");

                    foreach (var bad in new[] { ("NaN", "19", "4", "2", "72"), ("0", "19", "4", "2", "72"), ("65", "19", "-2", "2", "72") })
                    {
                        SetKtVInputs(mainVm.AnalyticsVm, bad.Item1, bad.Item2, bad.Item3, bad.Item4, bad.Item5);
                        InvokeCommand(mainVm.AnalyticsVm.ComputeKtVCommand);
                        PumpUi(window);
                        Assert(mainVm.AnalyticsVm.KtVResult is { IsValid: false } &&
                               !string.IsNullOrWhiteSpace(mainVm.AnalyticsVm.KtVErrorMessage),
                            $"Invalid Kt/V input ({bad.Item1}, {bad.Item3}) must reject cleanly with explicit error message.");
                    }

                    // Restore clean adequate calculation and test DryWeightLogExpander + restore Tab 1
                    SetKtVInputs(mainVm.AnalyticsVm, "65", "19", "4", "2", "72");
                    InvokeCommand(mainVm.AnalyticsVm.ComputeKtVCommand);
                    InvokeCommand(mainVm.AnalyticsVm.ToggleDryWeightLogCommand);
                    PumpUi(window);
                    Assert(dryWeightExpander.IsExpanded && dryWeightTimeline.IsEffectivelyVisible,
                        "ToggleDryWeightLogCommand must expand DryWeightLogExpander.");
                    InvokeCommand(mainVm.AnalyticsVm.ToggleKtvDrawerCommand);
                    InvokeCommand(mainVm.AnalyticsVm.ToggleDryWeightLogCommand);
                    InvokeCommand(mainVm.AnalyticsVm.SelectLabsTabCommand);
                    PumpUi(window);
                    Console.WriteLine("  -> PASS: Daugirdas II 1.429 adequate, 0.933 inadequate warning, 3 domain rejections, and DryWeightLogExpander verified.");

                    // -----------------------------------------------------------------
                    // ITERATIONS 11-13: ISMP Medication Typography & ADE Check
                    // -----------------------------------------------------------------
                    Console.WriteLine("[ITER 11/24] ISMP Safe Medication Typography Audit on SafetyRxWorkbenchView...");
                    mainVm.BannerVm.SelectPatientById(PatientAlphaId, notifyListeners: true);
                    InvokeCommand(mainVm.NavigateSafetyRxCommand);
                    PumpUi(window);

                    Assert(mainVm.SafetyRxVm.FormattedSampleTallManAlert.Contains("hydrOXYzine", StringComparison.Ordinal) &&
                           mainVm.SafetyRxVm.FormattedSampleTallManAlert.Contains("hydrALAzine", StringComparison.Ordinal) &&
                           mainVm.SafetyRxVm.FormattedSampleTallManAlert.Contains("HYDROmorphone", StringComparison.Ordinal) &&
                           mainVm.SafetyRxVm.FormattedSampleTallManAlert.Contains("0.5 mg", StringComparison.Ordinal) &&
                           mainVm.SafetyRxVm.FormattedSampleTallManAlert.Contains("1 L", StringComparison.Ordinal) &&
                           mainVm.SafetyRxVm.FormattedSampleTallManAlert.Contains("5000 units", StringComparison.Ordinal),
                        $"ISMP Tall Man/unit banner failed: '{mainVm.SafetyRxVm.FormattedSampleTallManAlert}'.");
                    CrossScreenGeometryAuditor.AuditIsmpTypography(safetyRxView, "SafetyRxWorkbenchView");
                    Console.WriteLine("  -> PASS: All SafetyRxWorkbenchView TextBlocks passed ISMP typography regex & Tall Man audit.");

                    Console.WriteLine("[ITER 12/24] ISMP Safe Medication Typography Audit on AnalyticsView & StickyPatientBanner...");
                    CrossScreenGeometryAuditor.AuditIsmpTypography(stickyBanner, "StickyPatientBanner");
                    InvokeCommand(mainVm.NavigateAnalyticsCommand);
                    PumpUi(window);
                    CrossScreenGeometryAuditor.AuditIsmpTypography(analyticsView, "AnalyticsView");
                    Console.WriteLine("  -> PASS: StickyPatientBanner and AnalyticsView passed ISMP typography audit.");

                    Console.WriteLine("[ITER 13/24] ADE Contraindication Probe (Heparin 5224 + Enoxaparin 67108 @ CrCl 10)...");
                    InvokeCommand(mainVm.NavigateSafetyRxCommand);
                    PumpUi(window);
                    var medAnemiaExpander = FindRequired<Expander>(safetyRxView, "MedicationAnemiaExpander");
                    Assert(!medAnemiaExpander.IsExpanded, "MedicationAnemiaExpander must start collapsed on SafetyRx load.");
                    medAnemiaExpander.IsExpanded = true;
                    PumpUi(window);
                    Assert(FindRequired<TextBox>(safetyRxView, "CandidateConcomitantRxTextBox").IsEffectivelyVisible,
                        "Expanding MedicationAnemiaExpander must reveal ADE drug check inputs.");

                    mainVm.SafetyRxVm.CandidateConcomitantRxCui = "67108";
                    mainVm.SafetyRxVm.EstimatedCrClText = "10";
                    InvokeCommand(mainVm.SafetyRxVm.EvaluateSafetyCommand);
                    PumpUi(window);

                    Assert(mainVm.SafetyRxVm.Conflicts.Count > 0 && mainVm.SafetyRxVm.HasAdeConflicts,
                        "Concurrent Heparin (5224) + Enoxaparin (67108) at CrCl 10 must produce ADE conflicts.");
                    Assert(mainVm.SafetyRxVm.OverallSafetyBadgeText == "BlockedContraindication",
                        $"Expected BlockedContraindication, got '{mainVm.SafetyRxVm.OverallSafetyBadgeText}'.");
                    mainVm.SafetyRxVm.CandidateConcomitantRxCui = string.Empty;
                    mainVm.SafetyRxVm.EstimatedCrClText = string.Empty;
                    InvokeCommand(mainVm.SafetyRxVm.EvaluateSafetyCommand);
                    medAnemiaExpander.IsExpanded = false;
                    PumpUi(window);
                    Console.WriteLine("  -> PASS: ADE tree blocked Enoxaparin contraindication cleanly inside expandable MedicationAnemiaExpander.");

                    // -----------------------------------------------------------------
                    // ITERATIONS 14-16: ECRI Zero Autopopulate & Append-Only SCD-2
                    // -----------------------------------------------------------------
                    Console.WriteLine("[ITER 14/24] ECRI Zero Autopopulate Audit & Opt-in FillStandardDefaults on SafetyRx...");
                    var newDwBox = FindRequired<TextBox>(safetyRxView, "NewDryWeightTextBox");
                    var dwRationaleBox = FindRequired<TextBox>(safetyRxView, "DryWeightRationaleTextBox");
                    var dialyzerBox = FindRequired<TextBox>(safetyRxView, "DialyzerModelTextBox");
                    var fillDefaultsBtn = FindRequired<Button>(safetyRxView, "FillStandardDefaultsButton");

                    Assert(string.IsNullOrEmpty(mainVm.SafetyRxVm.NewDryWeightKgText) &&
                           string.IsNullOrEmpty(newDwBox.Text) &&
                           string.IsNullOrEmpty(mainVm.SafetyRxVm.DryWeightRationaleText) &&
                           string.IsNullOrEmpty(dwRationaleBox.Text) &&
                           string.IsNullOrEmpty(mainVm.SafetyRxVm.DialyzerModel) &&
                           string.IsNullOrEmpty(dialyzerBox.Text) &&
                           string.IsNullOrEmpty(mainVm.SafetyRxVm.DurationMinText) &&
                           string.IsNullOrEmpty(mainVm.SafetyRxVm.QbText) &&
                           string.IsNullOrEmpty(mainVm.SafetyRxVm.QdText) &&
                           string.IsNullOrEmpty(mainVm.SafetyRxVm.AnticoagulantRxNormCui) &&
                           mainVm.SafetyRxVm.SelectedAnticoagulantCatalog is null,
                        "DryWeight and Prescription entry boxes (including Anticoagulant) MUST start empty (zero ECRI autopopulate).");

                    InvokeCommand(mainVm.SafetyRxVm.SaveDryWeightCommand);
                    PumpUi(window);
                    Assert(mainVm.SafetyRxVm.DryWeightStatusMessage.Contains("Validation error", StringComparison.OrdinalIgnoreCase),
                        "Blank Dry Weight submission must fail validation without mutating SCD-2 history.");

                    InvokeCommand(mainVm.SafetyRxVm.SignPrescriptionCommand);
                    PumpUi(window);
                    Assert(mainVm.SafetyRxVm.IsRxError &&
                           mainVm.SafetyRxVm.RxStatusMessage.Contains("Validation error", StringComparison.OrdinalIgnoreCase),
                        "Blank Prescription submission must fail validation without mutating SCD-2 history or falling back to defaults.");

                    Assert(fillDefaultsBtn.IsEffectivelyVisible, "FillStandardDefaultsButton must be visible on PrescriptionOrderCard.");
                    InvokeCommand(mainVm.SafetyRxVm.FillStandardDefaultsCommand);
                    PumpUi(window);
                    Assert(!string.IsNullOrWhiteSpace(mainVm.SafetyRxVm.DialyzerModel) &&
                           !string.IsNullOrWhiteSpace(mainVm.SafetyRxVm.DurationMinText) &&
                           !string.IsNullOrWhiteSpace(mainVm.SafetyRxVm.QbText) &&
                           !string.IsNullOrWhiteSpace(mainVm.SafetyRxVm.QdText),
                        "Opt-in FillStandardDefaultsButton must populate Rx draft fields explicitly.");
                    using (var scope = bootstrap.ScopeFactory.CreateScope())
                    {
                        var patientService = scope.ServiceProvider.GetRequiredService<IPatientService>();
                        Assert(patientService.GetPrescriptionHistory(PatientAlphaId).Count == 1,
                            "FillStandardDefaultsButton must only populate form fields and NEVER auto-sign or persist an Rx.");
                    }
                    Console.WriteLine("  -> PASS: ECRI zero-autopopulate verified; blank order blocked; opt-in FillStandardDefaults verified.");

                    Console.WriteLine("[ITER 15/24] Append-Only SCD-2 Dry Weight Update (73 -> 72.1 kg) & Live Banner Refresh...");
                    mainVm.SafetyRxVm.NewDryWeightKgText = "72.1";
                    mainVm.SafetyRxVm.DryWeightRationaleText = "Post-session euvolemia reached without cramping";
                    InvokeCommand(mainVm.SafetyRxVm.SaveDryWeightCommand);
                    PumpUi(window);

                    using (var scope = bootstrap.ScopeFactory.CreateScope())
                    {
                        var patientService = scope.ServiceProvider.GetRequiredService<IPatientService>();
                        var handshakeService = scope.ServiceProvider.GetRequiredService<IOrderHandshakeService>();

                        var dwHistory = patientService.GetDryWeightHistory(PatientAlphaId);
                        Assert(dwHistory.Count == 2 &&
                               dwHistory.Count(h => h.IsCurrent == 1) == 1 &&
                               Math.Abs(dwHistory.Single(h => h.IsCurrent == 1).TargetDryWeightKg - 72.1) < 0.001,
                            $"Expected 2 SCD-2 DryWeight rows with current=72.1, got count={dwHistory.Count}.");

                        var handshakes = handshakeService.GetDirectivesForPatient(PatientAlphaId);
                        Assert(handshakes.Any(h => h.DirectiveType == "DRY_WEIGHT_UPDATE" && h.Status == "PENDING_ACK"),
                            "Dry weight update must queue an OrderHandshake in PENDING_ACK status.");
                    }

                    Assert(bannerDryWeight.Text?.Contains("72.1 kg", StringComparison.Ordinal) == true,
                        $"Sticky banner dry weight must update live to 72.1 kg, got '{bannerDryWeight.Text}'.");
                    Assert(string.IsNullOrEmpty(mainVm.SafetyRxVm.NewDryWeightKgText),
                        "NewDryWeightKgText must reset to empty after append-only commit.");
                    Console.WriteLine("  -> PASS: SCD-2 DryWeight=72.1 kg appended (2 rows), Sticky Banner synced, PENDING_ACK queued.");

                    Console.WriteLine("[ITER 16/24] Append-Only Dialysis Prescription Sign-Off & Trigger SCD-2 Closure...");
                    mainVm.SafetyRxVm.DialyzerModel = "FX-100";
                    mainVm.SafetyRxVm.SurfaceAreaM2Text = "1.8";
                    mainVm.SafetyRxVm.DurationMinText = "240";
                    mainVm.SafetyRxVm.QbText = "350";
                    mainVm.SafetyRxVm.QdText = "500";
                    mainVm.SafetyRxVm.PotassiumMeqLText = "2";
                    mainVm.SafetyRxVm.CalciumMeqLText = "1.25";
                    mainVm.SafetyRxVm.SodiumMeqLText = "138";
                    mainVm.SafetyRxVm.BicarbonateMeqLText = "34";
                    mainVm.SafetyRxVm.HeparinBolusText = "1500";
                    mainVm.SafetyRxVm.HeparinHourlyText = "500";
                    mainVm.SafetyRxVm.PrescriptionRationaleText = "Standard high-flux adequacy optimization";
                    InvokeCommand(mainVm.SafetyRxVm.SignPrescriptionCommand);
                    PumpUi(window);

                    using (var scope = bootstrap.ScopeFactory.CreateScope())
                    {
                        var patientService = scope.ServiceProvider.GetRequiredService<IPatientService>();
                        var rxHistory = patientService.GetPrescriptionHistory(PatientAlphaId);
                        Assert(rxHistory.Count == 2 &&
                               rxHistory.Count(r => r.IsCurrent == 1) == 1 &&
                               rxHistory.Single(r => r.IsCurrent == 1).DialyzerMembraneModel == "FX-100",
                            $"Expected 2 SCD-2 Rx rows (prior closed by trg_ClosePreviousPrescription, current=FX-100), got count={rxHistory.Count}.");
                    }
                    Console.WriteLine("  -> PASS: Append-only Rx closed previous active prescription via DB trigger & queued handshake.");

                    // -----------------------------------------------------------------
                    // ITERATIONS 17-19: Desktop <-> Ephemeral :5050 Bridge & Conflict UI
                    // -----------------------------------------------------------------
                    Console.WriteLine("[ITER 17/24] Embedded Loopback Sync Host Health & Active Shift Bridge...");
                    InvokeCommand(mainVm.NavigateSyncMonitorCommand);
                    PumpUi(window);

                    var techExpander = FindRequired<Expander>(syncMonitorView, "TechnicalSyncExpander");
                    Assert(!techExpander.IsExpanded, "TechnicalSyncExpander must start collapsed on SyncMonitor.");
                    techExpander.IsExpanded = true;
                    PumpUi(window);

                    using var http = new HttpClient
                    {
                        BaseAddress = new Uri($"http://127.0.0.1:{freePort}"),
                        Timeout = TimeSpan.FromSeconds(5)
                    };

                    var healthResp = http.GetAsync("/api/v1/health").GetAwaiter().GetResult();
                    Assert(healthResp.StatusCode == HttpStatusCode.OK, $"GET /api/v1/health expected 200, got {healthResp.StatusCode}.");

                    var shiftResp = http.GetAsync($"/api/v1/shift/active?date={ShiftDate}&shift={ShiftNum}").GetAwaiter().GetResult();
                    Assert(shiftResp.StatusCode == HttpStatusCode.OK, $"GET /api/v1/shift/active expected 200, got {shiftResp.StatusCode}.");

                    mainVm.SyncMonitorVm.RefreshDiagnostics();
                    PumpUi(window);
                    Assert(mainVm.SyncMonitorVm.IsListening && mainVm.SyncMonitorVm.RequestsHandled >= 2 &&
                           FindRequired<Border>(syncMonitorView, "SyncHostStatusBadge").IsEffectivelyVisible,
                        $"SyncMonitorVm must report listening and >= 2 handled requests inside expanded TechnicalSyncExpander (got {mainVm.SyncMonitorVm.RequestsHandled}).");
                    techExpander.IsExpanded = false;
                    PumpUi(window);
                    Console.WriteLine($"  -> PASS: Loopback host :127.0.0.1:{freePort} handled {mainVm.SyncMonitorVm.RequestsHandled} requests.");

                    Console.WriteLine("[ITER 18/24] Live 409 Conflict Injection -> SyncMonitor Conflict Queue...");
                    var conflictFormPanel = FindRequired<StackPanel>(syncMonitorView, "ConflictResolutionFormPanel");
                    var calmStatusCard = FindRequired<Border>(syncMonitorView, "CalmOrderStatusCard");

                    string conflictHandshakeId;
                    using (var scope = bootstrap.ScopeFactory.CreateScope())
                    {
                        var handshakeService = scope.ServiceProvider.GetRequiredService<IOrderHandshakeService>();
                        conflictHandshakeId = handshakeService.GetDirectivesForPatient(PatientAlphaId)
                            .First(h => h.DirectiveType == "DRY_WEIGHT_UPDATE" && h.Status == "PENDING_ACK")
                            .HandshakeId;
                    }

                    mainVm.SyncMonitorVm.FocusDirective(conflictHandshakeId);
                    PumpUi(window);
                    Assert(mainVm.SyncMonitorVm.IsSelectedItemWaiting &&
                           calmStatusCard.IsVisible && !conflictFormPanel.IsVisible,
                        "Waiting PENDING_ACK directive must show CalmOrderStatusCard and hide ConflictResolutionFormPanel.");

                    string ackJson = JsonSerializer.Serialize(
                        new MobileHandshakeAckRequest(
                            NurseStaffId: "NR-MEND-01",
                            ReportedSyncVersion: 999),
                        WebJsonOptions);
                    using (var ackContent = new StringContent(ackJson, Encoding.UTF8, "application/json"))
                    {
                        var ackResp = http.PostAsync($"/api/v1/handshakes/{conflictHandshakeId}/ack", ackContent)
                            .GetAwaiter().GetResult();
                        Assert(ackResp.StatusCode == HttpStatusCode.Conflict,
                            $"Stale sync version 999 must return HTTP 409 Conflict, got {ackResp.StatusCode}.");
                    }

                    mainVm.SyncMonitorVm.RefreshDiagnostics();
                    mainVm.SyncMonitorVm.FocusDirective(conflictHandshakeId);
                    PumpUi(window);
                    Assert(mainVm.SyncMonitorVm.ConflictCount >= 1 &&
                           mainVm.SyncMonitorVm.ConflictQueueItems.Any(c =>
                               c.HandshakeId == conflictHandshakeId &&
                               c.Status == "CONFLICT_REQUIRES_CLINICIAN_ACK"),
                        "SyncMonitorVm must display the 409 conflicted OrderHandshake in ConflictQueueItems.");
                    Assert(conflictFormPanel.IsVisible && !calmStatusCard.IsVisible,
                        "409 conflict selection must reveal ConflictResolutionFormPanel and hide CalmOrderStatusCard.");

                    CrossScreenGeometryAuditor.AuditNoRedOnTechnicalSync(syncMonitorView, "SyncMonitorView");
                    Console.WriteLine("  -> PASS: 409 Conflict surfaced in SyncMonitor Conflict Queue with Steady Cyan/Amber (zero Red).");

                    Console.WriteLine("[ITER 19/24] Clinician Conflict Resolution Rationale Guardrail -> ACKNOWLEDGED...");
                    mainVm.SyncMonitorVm.SelectedConflict = mainVm.SyncMonitorVm.ConflictQueueItems
                        .First(c => c.HandshakeId == conflictHandshakeId);
                    mainVm.SyncMonitorVm.ResolutionNotes = string.Empty;
                    Assert(!mainVm.SyncMonitorVm.CanResolveSelectedConflict &&
                           !mainVm.SyncMonitorVm.ResolveConflictCommand.CanExecute(null),
                        "Empty clinician rationale must disable ResolveConflictCommand.");
                    InvokeCommand(mainVm.SyncMonitorVm.ResolveConflictCommand);
                    PumpUi(window);
                    Assert(mainVm.SyncMonitorVm.LastResolvedHandshake is null &&
                           mainVm.SyncMonitorVm.ConflictQueueItems.Any(c =>
                               c.HandshakeId == conflictHandshakeId &&
                               c.Status == "CONFLICT_REQUIRES_CLINICIAN_ACK"),
                        "Empty clinician rationale must block conflict resolution and keep directive in CONFLICT_REQUIRES_CLINICIAN_ACK.");

                    mainVm.SyncMonitorVm.ClinicianStaffId = "DR-MEND-01";
                    mainVm.SyncMonitorVm.ResolutionNotes = "Bedside tablet synced to 72.1 kg order; discrepancy reviewed.";
                    InvokeCommand(mainVm.SyncMonitorVm.ResolveConflictCommand);
                    PumpUi(window);

                    Assert(mainVm.SyncMonitorVm.LastResolvedHandshake is not null &&
                           mainVm.SyncMonitorVm.LastResolvedHandshake.HandshakeId == conflictHandshakeId &&
                           mainVm.SyncMonitorVm.LastResolvedHandshake.Status == "ACKNOWLEDGED",
                        $"Resolved handshake must transition to ACKNOWLEDGED, got '{mainVm.SyncMonitorVm.LastResolvedHandshake?.Status}'.");
                    Assert(!conflictFormPanel.IsVisible && calmStatusCard.IsVisible,
                        "After resolving 409 conflict, ConflictResolutionFormPanel must hide and CalmOrderStatusCard must reappear.");
                    Console.WriteLine("  -> PASS: Clinician resolved 409 conflict with rationale -> ACKNOWLEDGED.");

                    // -----------------------------------------------------------------
                    // ITERATIONS 20-21: FDA Criterion 4, Deferred Card & FHIR R4 Bundle
                    // -----------------------------------------------------------------
                    Console.WriteLine("[ITER 20/24] FDA Criterion 4 Split Pane, Top-3 Driver Bars & Hardware-Deferred Notice...");
                    InvokeCommand(mainVm.NavigateSafetyRxCommand);
                    PumpUi(window);

                    var top3Control = FindRequired<ItemsControl>(safetyRxView, "Top3DriversItemsControl");
                    var reasoningExpander = FindRequired<Expander>(safetyRxView, "ClinicalReasoningExpander");
                    var detPane = FindRequired<Border>(safetyRxView, "DeterministicBasisPane");
                    var qualPane = FindRequired<Border>(safetyRxView, "QualitativeGuidancePane");

                    Assert(mainVm.SafetyRxVm.Top3Drivers.Count > 0 && mainVm.SafetyRxVm.Top3Drivers.Count <= 3,
                        $"Expected 1..3 Top3Drivers, got {mainVm.SafetyRxVm.Top3Drivers.Count}.");
                    Assert(top3Control.Bounds.Width > 200 && top3Control.Bounds.Height > 20,
                        $"Top3DriversItemsControl rendered with invalid bounds {top3Control.Bounds}.");
                    bool hasRawDriverShorest = top3Control.GetVisualDescendants()
                        .OfType<TextBlock>()
                        .Any(tb => tb.Text is not null &&
                                   (tb.Text.Contains("Nadir90", StringComparison.Ordinal) ||
                                    tb.Text.Contains("dMAP", StringComparison.Ordinal)));
                    Assert(!hasRawDriverShorest,
                        "Top3DriversItemsControl must humanize 'Nadir90' -> 'Lowest SBP' and 'dMAP' -> 'BP Drop'.");

                    Assert(!reasoningExpander.IsExpanded, "ClinicalReasoningExpander must start collapsed on SafetyRx.");
                    reasoningExpander.IsExpanded = true;
                    PumpUi(window);

                    Assert(detPane.IsEffectivelyVisible && qualPane.IsEffectivelyVisible,
                        "Both Criterion 4 split panes must be visible when ClinicalReasoningExpander is expanded.");
                    Assert(detPane.Bounds.Intersect(qualPane.Bounds).Width <= 1.0,
                        "DeterministicBasisPane and QualitativeGuidancePane must be visibly separated without overlap.");
                    Assert(mainVm.SafetyRxVm.VerifiedDeterministicCitations.Any(c =>
                               c.Contains("KDIGO", StringComparison.OrdinalIgnoreCase) ||
                               c.Contains("KDOQI", StringComparison.OrdinalIgnoreCase) ||
                               c.Contains("MIMIC", StringComparison.OrdinalIgnoreCase) ||
                               c.Contains("Flythe", StringComparison.OrdinalIgnoreCase)),
                        "VerifiedDeterministicCitations must cite accredited guidelines.");

                    InvokeCommand(mainVm.NavigateAnalyticsCommand);
                    InvokeCommand(mainVm.AnalyticsVm.SelectSessionsTabCommand);
                    PumpUi(window);
                    var deferredCard = FindRequired<Border>(analyticsView, "HardwareDeferredCard");
                    Assert(deferredCard.IsEffectivelyVisible &&
                           mainVm.AnalyticsVm.HardwareDeferredNoticeTitle.Contains("Hardware Telemetry Deferred", StringComparison.Ordinal),
                        "AnalyticsView Tab 2 (Sessions & Kt/V) must display honest Hardware Telemetry Deferred notice card.");
                    InvokeCommand(mainVm.AnalyticsVm.SelectLabsTabCommand);
                    PumpUi(window);
                    Console.WriteLine("  -> PASS: FDA Criterion 4 split panes, humanized Top-3 bars, and Hardware-Deferred notice verified.");

                    Console.WriteLine("[ITER 21/24] HL7 FHIR R4 Bundle Preview & JSON Structural Verification...");
                    InvokeCommand(mainVm.NavigateSafetyRxCommand);
                    reasoningExpander.IsExpanded = true;
                    PumpUi(window);
                    InvokeCommand(mainVm.SafetyRxVm.ExportFhirBundleCommand);
                    PumpUi(window);

                    var fhirBox = FindRequired<TextBox>(safetyRxView, "FhirJsonViewerBox");
                    Assert(mainVm.SafetyRxVm.IsFhirDrawerExpanded && fhirBox.IsEffectivelyVisible,
                        "FHIR R4 JSON drawer must expand on ExportFhirBundleCommand inside ClinicalReasoningExpander.");
                    using (var fhirDoc = JsonDocument.Parse(mainVm.SafetyRxVm.FhirBundleJsonText))
                    {
                        string? resourceType = fhirDoc.RootElement.GetProperty("resourceType").GetString();
                        Assert(resourceType == "Bundle", $"Expected FHIR resourceType='Bundle', got '{resourceType}'.");
                    }
                    Assert(mainVm.SafetyRxVm.FhirBundleJsonText.Contains("99737-9", StringComparison.Ordinal) &&
                           mainVm.SafetyRxVm.FhirBundleJsonText.Contains("70965-9", StringComparison.Ordinal),
                        "FHIR R4 Bundle JSON must include LOINC 99737-9 and 70965-9.");
                    mainVm.SafetyRxVm.IsFhirDrawerExpanded = false;
                    reasoningExpander.IsExpanded = false;
                    PumpUi(window);
                    Console.WriteLine("  -> PASS: HL7 FHIR R4 Bundle JSON validated.");

                    var allGeometryViolations = new List<string>();

                    // -----------------------------------------------------------------
                    // ITERATIONS 25-30: Wave 1 Clinical UX, Shortcuts, Sync & Distinct Beds
                    // -----------------------------------------------------------------
                    Wave1ClinicalUxRegressionTests.RunPreViewportWave1Checks(
                        window,
                        mainVm,
                        bootstrap.ScopeFactory,
                        PatientAlphaId,
                        PatientAlphaMrn,
                        PatientBetaId,
                        PatientBetaMrn,
                        PatientNomId,
                        allGeometryViolations);

                    // -----------------------------------------------------------------
                    // ITERATIONS 34-37: Wave 2 Visual Polish, Sanitation & Contrast Checks
                    // -----------------------------------------------------------------
                    Wave2ClinicalUxRegressionTests.RunPreViewportWave2Checks(
                        window,
                        mainVm,
                        bootstrap.ScopeFactory,
                        PatientAlphaId,
                        PatientBetaId,
                        PatientNomId,
                        longitudinalMendeleyPatientId,
                        allGeometryViolations);

                    // -----------------------------------------------------------------
                    // ITERATIONS 39-41: Wave 3 Final Clinical Micro-Polish Checks
                    // -----------------------------------------------------------------
                    Wave3ClinicalUxRegressionTests.RunPreViewportWave3Checks(
                        window,
                        mainVm,
                        bootstrap.ScopeFactory,
                        PatientAlphaId,
                        PatientBetaId,
                        PatientNomId,
                        allGeometryViolations);

                    // -----------------------------------------------------------------
                    // ITERATIONS 22-24 & 31-33: Responsive Viewports + Geometry + Chart Interaction
                    // -----------------------------------------------------------------

                    Console.WriteLine("[ITER 22/24] Cross-Screen Geometry Audit & Screenshots @ 1024x700...");
                    capturedScreenshots.AddRange(
                        AuditAllTabsAtViewport(window, mainVm, 1024, 700, "1024x700", captureScreenshots: true, allGeometryViolations));
                    Console.WriteLine("  -> Completed 1024x700 audit & PNG captures.");

                    Console.WriteLine("[ITER 23/24] Cross-Screen Geometry Audit & Screenshots @ 1366x768 (+ Multi-Point Polylines)...");
                    capturedScreenshots.AddRange(
                        AuditAllTabsAtViewport(window, mainVm, 1366, 768, "1366x768", captureScreenshots: true, allGeometryViolations));

                    // Capture extra multi-point Analytics polyline view on longitudinal Mendeley patient (PT-MEND-0018)
                    mainVm.BannerVm.SelectPatientById(longitudinalMendeleyPatientId, notifyListeners: true);
                    InvokeCommandForTab(mainVm, WorkspaceTab.PatientAnalytics);
                    InvokeCommand(mainVm.AnalyticsVm.SelectLabsTabCommand);
                    mainVm.AnalyticsVm.IsAddLabDrawerOpen = false;
                    PumpUi(window);
                    Assert(mainVm.AnalyticsVm.SelectedSessionWindowCount == 20 &&
                           mainVm.AnalyticsVm.IsWindow20Selected &&
                           FindRequired<Button>(analyticsView, "Window20Button").Classes.Contains("active"),
                        "Expected Window20Button active highlight when SelectedSessionWindowCount == 20.");

                    mainVm.AnalyticsVm.SelectedAddLabCatalog =
                        mainVm.AnalyticsVm.CatalogLoincItems.FirstOrDefault(c => c.LoincCode == "2823-3");
                    mainVm.AnalyticsVm.AddLabLoincCode = "2823-3";
                    mainVm.AnalyticsVm.AddLabPhase = "Pre-Dialysis";
                    mainVm.AnalyticsVm.AddLabValue = "5.2";
                    InvokeCommand(mainVm.AnalyticsVm.SaveLabResultCommand);
                    PumpUi(window);

                    mainVm.AnalyticsVm.AddLabValue = "6.5";
                    InvokeCommand(mainVm.AnalyticsVm.SaveLabResultCommand);
                    PumpUi(window);

                    var analyticsSv = FindRequired<ScrollViewer>(analyticsView, "AnalyticsScrollViewer");
                    analyticsSv.Offset = default;
                    window.FocusManager?.ClearFocus();
                    PumpUi(window);

                    allGeometryViolations.AddRange(
                        CrossScreenGeometryAuditor.CollectWindowGeometryViolations(window, "1366x768:PatientAnalyticsMultiPoint"));
                    try
                    {
                        CrossScreenGeometryAuditor.AuditNoDeveloperJargon(analyticsView, "1366x768:PatientAnalyticsMultiPoint");
                        CrossScreenGeometryAuditor.AuditIsmpTypography(analyticsView, "1366x768:PatientAnalyticsMultiPoint");
                    }
                    catch (Exception ex)
                    {
                        allGeometryViolations.Add(ex.Message);
                    }

                    using (var multiFrame = window.CaptureRenderedFrame())
                    {
                        Assert(multiFrame is not null && multiFrame.PixelSize.Width > 0 && multiFrame.PixelSize.Height > 0,
                            "CaptureRenderedFrame returned empty frame for 1366x768:PatientAnalyticsMultiPoint.");
                        string multiPath = Path.Combine(ScreenshotDirectory, "02b_patient_analytics_multipoint_1366x768.png");
                        multiFrame!.Save(multiPath);
                        capturedScreenshots.Add(multiPath);
                    }

                    try
                    {
                        string tooltipShot = Wave1ClinicalUxRegressionTests.RunInteractiveChartAndSameDayChecks(
                            window,
                            mainVm.AnalyticsVm,
                            analyticsView,
                            ScreenshotDirectory);
                        capturedScreenshots.Add(tooltipShot);
                        Wave2ClinicalUxRegressionTests.VerifyWave2HoverTooltipGeometry(window, mainVm.AnalyticsVm, analyticsView);
                    }
                    catch (Exception ex)
                    {
                        allGeometryViolations.Add($"[ITER31:ChartInteraction] {ex.Message}");
                    }

                    analyticsSv.Offset = default;
                    mainVm.BannerVm.SelectPatientById(PatientAlphaId, notifyListeners: true);
                    PumpUi(window);
                    Console.WriteLine("  -> Completed 1366x768 audit & PNG captures (including 02b multi-point & 02c interactive tooltip views).");

                    Console.WriteLine("[ITER 23B/24] Cross-Screen Geometry Audit & Wave 4 Captures @ 1366x820 (Above-the-Fold SignRx <= 790px)...");
                    capturedScreenshots.AddRange(
                        AuditAllTabsAtViewport(window, mainVm, 1366, 820, "1366x820", captureScreenshots: true, allGeometryViolations));
                    capturedScreenshots.AddRange(
                        CaptureWave4FocusedScreenshots(window, mainVm, bootstrap.ScopeFactory, longitudinalMendeleyPatientId, allGeometryViolations));
                    Console.WriteLine("  -> Completed 1366x820 audit & focused Wave 4 PNG captures.");

                    Console.WriteLine("[ITER 24/24] Cross-Screen Geometry Audit @ 1920x1080 & Clean Teardown...");
                    AuditAllTabsAtViewport(window, mainVm, 1920, 1080, "1920x1080", captureScreenshots: false, allGeometryViolations);
                    Console.WriteLine("  -> Completed 1920x1080 audit.");

                    if (allGeometryViolations.Count > 0)
                    {
                        foreach (string shot in capturedScreenshots)
                        {
                            Console.WriteLine($"   [PNG] {shot}");
                        }

                        throw new InvalidOperationException(
                            $"[GEOMETRY AUDIT FAILED] {allGeometryViolations.Count} issue(s) across viewports (PNGs saved: {capturedScreenshots.Count}):\n - " +
                            string.Join("\n - ", allGeometryViolations));
                    }
                }
                finally
                {
                    window.Close();
                    Dispatcher.UIThread.RunJobs();
                }

                // Explicit socket release verification after disposing host
                bootstrap.SyncHost?.Dispose();
                Assert(bootstrap.SyncHost is null || !bootstrap.SyncHost.IsListening,
                    "LocalSyncHttpHost must report IsListening=false after Dispose.");
            }, CancellationToken.None).GetAwaiter().GetResult();

            // Verify OS port is free immediately after host/session disposal
            var verifyPortFree = new TcpListener(IPAddress.Loopback, freePort);
            try
            {
                verifyPortFree.Start();
            }
            finally
            {
                verifyPortFree.Stop();
            }

            Console.WriteLine("================================================================================");
            Console.WriteLine($" ALL 41/41 HEADLESS UX, INTERACTION & GEOMETRY ITERATIONS PASSED! Captures: {capturedScreenshots.Count}");
            foreach (string shot in capturedScreenshots)
            {
                Console.WriteLine($"   [PNG] {shot}");
            }
            Console.WriteLine("================================================================================");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch (IOException)
                {
                    // Best-effort cleanup if SQLite handle lingers briefly on Windows
                }
            }
        }
    }

    private static IReadOnlyList<string> AuditAllTabsAtViewport(
        MainWindow window,
        MainWindowViewModel mainVm,
        double width,
        double height,
        string label,
        bool captureScreenshots,
        List<string> violationAccumulator)
    {
        var files = new List<string>();
        window.Width = width;
        window.Height = height;
        PumpUi(window);

        var tabSpecs = new (WorkspaceTab Tab, string Slug)[]
        {
            (WorkspaceTab.ShiftGrid, "01_shift_grid"),
            (WorkspaceTab.PatientAnalytics, "02_patient_analytics"),
            (WorkspaceTab.SafetyRxWorkbench, "03_safety_rx"),
            (WorkspaceTab.SyncMonitor, "04_sync_monitor")
        };

        foreach (var (tab, slug) in tabSpecs)
        {
            InvokeCommandForTab(mainVm, tab);
            PumpUi(window);
            ResetAllScrollViewersToHome(window);
            PumpUi(window);

            violationAccumulator.AddRange(
                CrossScreenGeometryAuditor.CollectWindowGeometryViolations(window, $"{label}:{tab}"));

            try
            {
                Wave1ClinicalUxRegressionTests.AuditViewportTabWave1Invariants(
                    window,
                    mainVm,
                    tab,
                    width,
                    height,
                    label);
                Wave2ClinicalUxRegressionTests.AuditViewportTabWave2Invariants(
                    window,
                    mainVm,
                    tab,
                    width,
                    height,
                    label);
                Wave3ClinicalUxRegressionTests.AuditViewportTabWave3Invariants(
                    window,
                    tab,
                    width,
                    label);
            }
            catch (Exception ex)
            {
                violationAccumulator.Add(ex.Message);
            }

            if (captureScreenshots)
            {
                using var frame = window.CaptureRenderedFrame();
                Assert(frame is not null && frame.PixelSize.Width > 0 && frame.PixelSize.Height > 0,
                    $"CaptureRenderedFrame returned empty frame for {label}:{tab}.");

                string pngPath = Path.Combine(ScreenshotDirectory, $"{slug}_{label}.png");
                frame!.Save(pngPath);
                files.Add(pngPath);
            }
        }

        return files;
    }

    private static void ResetAllScrollViewersToHome(Window window)
    {
        window.FocusManager?.ClearFocus();
        foreach (ScrollViewer sv in window.GetVisualDescendants().OfType<ScrollViewer>())
        {
            sv.ScrollToHome();
            sv.SetCurrentValue(ScrollViewer.OffsetProperty, default(Vector));
        }
    }

    private static void SeedDeterministicUxPatients(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();
        var patients = scope.ServiceProvider.GetRequiredService<IPatientRepository>();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
        var labs = scope.ServiceProvider.GetRequiredService<ILabRepository>();
        var incidents = scope.ServiceProvider.GetRequiredService<IIncidentRepository>();

        catalogs.UpsertStaffMember(new StaffMemberRecord(
            StaffId: "DR-MEND-01",
            FirstName: "Tarek",
            MiddleName: "Hassan",
            LastName: "El-Masry",
            RoleName: "Doctor",
            LicenseNumber: "EG-NEPH-88412",
            PasswordHash: "DEFERRED_POST_PROTOTYPE",
            IsActive: 1,
            CreatedAtUtc: "2026-11-01T08:00:00Z"));

        catalogs.UpsertStaffMember(new StaffMemberRecord(
            StaffId: "NR-MEND-01",
            FirstName: "Mona",
            MiddleName: "Adel",
            LastName: "Kamel",
            RoleName: "Nurse",
            LicenseNumber: "EG-RN-44109",
            PasswordHash: "DEFERRED_POST_PROTOTYPE",
            IsActive: 1,
            CreatedAtUtc: "2026-11-01T08:00:00Z"));

        // 1. Patient Alpha: HBV+ Isolation, HighRed SBP=84 bed, baseline DryWeight 73.0 kg, Hb lab for AnemiaCare HD
        patients.InsertPatient(new PatientRecord(
            PatientId: PatientAlphaId,
            MedicalRecordNumber: PatientAlphaMrn,
            FirstName: "Amira",
            MiddleName: "Hassan",
            LastName: "El-Masry",
            DateOfBirth: "1969-03-18",
            BiologicalSex: "F",
            HeightCm: 162.0,
            BloodType: "A+",
            DialysisStartDate: "2021-09-12",
            PrimaryEtiologyCode: "E11.22",
            AttendingDoctorId: "DR-MEND-01",
            HepatitisBStatus: 1,
            IsolationRequired: 1,
            IsActive: 1,
            CreatedAtUtc: "2026-10-01T08:00:00Z",
            UpdatedAtUtc: "2026-10-01T08:00:00Z"));

        patients.AppendVascularAccessHistory(new VascularAccessHistoryRecord(
            AccessHistoryId: 0,
            PatientId: PatientAlphaId,
            AccessType: "AV_Fistula",
            AnatomicalSite: "Left_Forearm_Radiocephalic",
            MaxBloodFlowRateMlMin: 400.0,
            EffectiveFromUtc: "2021-08-01T00:00:00Z",
            EffectiveToUtc: null,
            IsCurrent: 1,
            AssessedByStaffId: "DR-MEND-01"));

        patients.AppendDryWeightHistory(new DryWeightHistoryRecord(
            DryWeightHistoryId: 0,
            PatientId: PatientAlphaId,
            TargetDryWeightKg: 73.0,
            EffectiveFromUtc: "2026-10-01T08:00:00Z",
            EffectiveToUtc: null,
            IsCurrent: 1,
            PrescribedByDoctorId: "DR-MEND-01",
            ClinicalRationale: "Baseline UX test dry weight"));

        patients.InsertPrescription(new DialysisPrescriptionRecord(
            PrescriptionId: "RX-UX-ALPHA-01",
            PatientId: PatientAlphaId,
            DialyzerMembraneModel: "FX-80",
            SurfaceAreaM2: 1.8,
            TargetDurationMinutes: 240.0,
            PrescribedBloodFlowMlMin: 340.0,
            PrescribedDialysateFlowMlMin: 500.0,
            DialysatePotassiumMeqL: 2.0,
            DialysateCalciumMeqL: 1.25,
            DialysateSodiumMeqL: 138.0,
            DialysateBicarbonateMeqL: 34.0,
            AnticoagulantRxNormCui: "5224",
            AnticoagulantBolusUnits: 2000.0,
            AnticoagulantHourlyUnits: 500.0,
            IsCurrent: 1,
            PrescribedByDoctorId: "DR-MEND-01",
            EffectiveFromUtc: "2026-10-01T08:00:00Z"));

        labs.InsertLabOrder(new LabOrderRecord(
            LabOrderId: "ORD-UX-HB-1",
            PatientId: PatientAlphaId,
            SessionId: null,
            OrderedByDoctorId: "DR-MEND-01",
            SpecimenCollectionUtc: "2026-11-05T07:00:00Z",
            SamplingPhase: "Pre-Dialysis",
            LabPanelName: "Anemia Panel",
            OrderStatus: "Resulted"));

        labs.InsertLabResult(new LabResultRecord(
            LabResultId: "RES-UX-HB-1",
            LabOrderId: "ORD-UX-HB-1",
            PatientId: PatientAlphaId,
            LoincCode: "718-7",
            NumericValue: 9.6,
            AbnormalityFlag: "Low",
            VerifiedByLabTech: 1,
            ResultedAtUtc: "2026-11-05T07:30:00Z"));

        sessions.InsertSession(new DialysisSessionRecord(
            SessionId: "SES-UX-RED",
            PatientId: PatientAlphaId,
            PrescriptionId: "RX-UX-ALPHA-01",
            AssignedNurseId: "NR-MEND-01",
            SupervisingDoctorId: "DR-MEND-01",
            ChairOrBedNumber: "BED-UX-01",
            ShiftNumber: ShiftNum,
            SessionDate: ShiftDate,
            StartTimeUtc: $"{ShiftDate}T08:00:00Z",
            EndTimeUtc: null,
            SessionStatus: "InProgress",
            PreDialysisWeightKg: 75.8,
            PreSystolicBp: 140,
            PreDiastolicBp: 86,
            PreHeartRateBpm: 78,
            PreTemperatureCelsius: 36.7,
            TargetUltrafiltrationMl: 2800.0,
            PostDialysisWeightKg: null,
            PostSystolicBp: null,
            PostDiastolicBp: null,
            ActualUltrafiltrationMl: null,
            CalculatedSpKtV: null,
            CalculatedUrrPercent: null,
            SyncVersion: 1,
            LastModifiedUtc: $"{ShiftDate}T08:00:00Z"));

        sessions.AppendTelemetryTick(new IntradialyticTelemetryRecord(
            TelemetryId: 0,
            SessionId: "SES-UX-RED",
            RecordedAtUtc: $"{ShiftDate}T09:45:00Z",
            ElapsedMinutes: 105,
            SystolicBp: 84,
            DiastolicBp: 52,
            HeartRateBpm: 106,
            BloodFlowRateMlMin: 340.0,
            DialysateFlowRateMlMin: 500.0,
            VenousPressureMmHg: 145.0,
            ArterialPressureMmHg: -150.0,
            TransmembranePressureMmHg: 90.0,
            UltrafiltrationRateMlHr: 700.0,
            CumulativeUfRemovedMl: 1225.0,
            DialysateConductivityMsCm: 13.8,
            DialysateTemperatureCelsius: 36.5));

        // 2. Patient Beta: Standard Precautions, ZERO labs initially, MediumYellow warning bed
        patients.InsertPatient(new PatientRecord(
            PatientId: PatientBetaId,
            MedicalRecordNumber: PatientBetaMrn,
            FirstName: "Karim",
            MiddleName: "Nabil",
            LastName: "Farouk",
            DateOfBirth: "1974-11-22",
            BiologicalSex: "M",
            HeightCm: 176.0,
            BloodType: "O+",
            DialysisStartDate: "2023-05-14",
            PrimaryEtiologyCode: "I12.0",
            AttendingDoctorId: "DR-MEND-01",
            HepatitisBStatus: 0,
            IsolationRequired: 0,
            IsActive: 1,
            CreatedAtUtc: "2026-10-01T08:00:00Z",
            UpdatedAtUtc: "2026-10-01T08:00:00Z"));

        patients.AppendDryWeightHistory(new DryWeightHistoryRecord(
            DryWeightHistoryId: 0,
            PatientId: PatientBetaId,
            TargetDryWeightKg: 80.5,
            EffectiveFromUtc: "2026-10-01T08:00:00Z",
            EffectiveToUtc: null,
            IsCurrent: 1,
            PrescribedByDoctorId: "DR-MEND-01",
            ClinicalRationale: "Baseline dry weight"));

        patients.InsertPrescription(new DialysisPrescriptionRecord(
            PrescriptionId: "RX-UX-BETA-01",
            PatientId: PatientBetaId,
            DialyzerMembraneModel: "FX-80",
            SurfaceAreaM2: 1.8,
            TargetDurationMinutes: 240.0,
            PrescribedBloodFlowMlMin: 320.0,
            PrescribedDialysateFlowMlMin: 500.0,
            DialysatePotassiumMeqL: 2.0,
            DialysateCalciumMeqL: 1.25,
            DialysateSodiumMeqL: 138.0,
            DialysateBicarbonateMeqL: 34.0,
            AnticoagulantRxNormCui: "5224",
            AnticoagulantBolusUnits: 1500.0,
            AnticoagulantHourlyUnits: 500.0,
            IsCurrent: 1,
            PrescribedByDoctorId: "DR-MEND-01",
            EffectiveFromUtc: "2026-10-01T08:00:00Z"));

        sessions.InsertSession(new DialysisSessionRecord(
            SessionId: "SES-UX-AMB",
            PatientId: PatientBetaId,
            PrescriptionId: "RX-UX-BETA-01",
            AssignedNurseId: "NR-MEND-01",
            SupervisingDoctorId: "DR-MEND-01",
            ChairOrBedNumber: "BED-UX-02",
            ShiftNumber: ShiftNum,
            SessionDate: ShiftDate,
            StartTimeUtc: $"{ShiftDate}T08:00:00Z",
            EndTimeUtc: null,
            SessionStatus: "InProgress",
            PreDialysisWeightKg: 82.0,
            PreSystolicBp: 132,
            PreDiastolicBp: 82,
            PreHeartRateBpm: 76,
            PreTemperatureCelsius: 36.6,
            TargetUltrafiltrationMl: 1500.0,
            PostDialysisWeightKg: null,
            PostSystolicBp: null,
            PostDiastolicBp: null,
            ActualUltrafiltrationMl: null,
            CalculatedSpKtV: null,
            CalculatedUrrPercent: null,
            SyncVersion: 1,
            LastModifiedUtc: $"{ShiftDate}T08:00:00Z"));

        sessions.AppendTelemetryTick(new IntradialyticTelemetryRecord(
            TelemetryId: 0,
            SessionId: "SES-UX-AMB",
            RecordedAtUtc: $"{ShiftDate}T09:30:00Z",
            ElapsedMinutes: 90,
            SystolicBp: 120,
            DiastolicBp: 76,
            HeartRateBpm: 82,
            BloodFlowRateMlMin: 320.0,
            DialysateFlowRateMlMin: 500.0,
            VenousPressureMmHg: 130.0,
            ArterialPressureMmHg: -140.0,
            TransmembranePressureMmHg: 80.0,
            UltrafiltrationRateMlHr: 375.0,
            CumulativeUfRemovedMl: 560.0,
            DialysateConductivityMsCm: 13.8,
            DialysateTemperatureCelsius: 36.5));

        incidents.InsertIncident(new ClinicalIncidentRecord(
            IncidentId: "INC-UX-AMB-01",
            SessionId: "SES-UX-AMB",
            PatientId: PatientBetaId,
            ReportedByStaffId: "NR-MEND-01",
            IncidentCategory: "MuscleCramps",
            SeverityLevel: "Moderate",
            Description: "Mild calf cramping observed; monitoring",
            ImmediateIntervention: "Reduced UFR temporarily",
            TriggerSystolicBp: 120,
            TriggerUfRateMlHr: 375.0,
            IsResolved: 0,
            OccurredAtUtc: $"{ShiftDate}T09:35:00Z",
            ResolvedAtUtc: null));

        // 3. Patient Nom: Strictly Nominal ● STABLE bed
        patients.InsertPatient(new PatientRecord(
            PatientId: PatientNomId,
            MedicalRecordNumber: "MRN-UX-NOM",
            FirstName: "Salma",
            MiddleName: "Adel",
            LastName: "Mansour",
            DateOfBirth: "1981-01-10",
            BiologicalSex: "F",
            HeightCm: 166.0,
            BloodType: "B+",
            DialysisStartDate: "2024-01-15",
            PrimaryEtiologyCode: "N18.6",
            AttendingDoctorId: "DR-MEND-01",
            HepatitisBStatus: 0,
            IsolationRequired: 0,
            IsActive: 1,
            CreatedAtUtc: "2026-10-01T08:00:00Z",
            UpdatedAtUtc: "2026-10-01T08:00:00Z"));

        patients.AppendDryWeightHistory(new DryWeightHistoryRecord(
            DryWeightHistoryId: 0,
            PatientId: PatientNomId,
            TargetDryWeightKg: 65.0,
            EffectiveFromUtc: "2026-10-01T08:00:00Z",
            EffectiveToUtc: null,
            IsCurrent: 1,
            PrescribedByDoctorId: "DR-MEND-01",
            ClinicalRationale: "Baseline nominal dry weight"));

        patients.InsertPrescription(new DialysisPrescriptionRecord(
            PrescriptionId: "RX-UX-NOM-01",
            PatientId: PatientNomId,
            DialyzerMembraneModel: "FX-60",
            SurfaceAreaM2: 1.4,
            TargetDurationMinutes: 240.0,
            PrescribedBloodFlowMlMin: 300.0,
            PrescribedDialysateFlowMlMin: 500.0,
            DialysatePotassiumMeqL: 2.0,
            DialysateCalciumMeqL: 1.25,
            DialysateSodiumMeqL: 138.0,
            DialysateBicarbonateMeqL: 34.0,
            AnticoagulantRxNormCui: "5224",
            AnticoagulantBolusUnits: 1000.0,
            AnticoagulantHourlyUnits: 500.0,
            IsCurrent: 1,
            PrescribedByDoctorId: "DR-MEND-01",
            EffectiveFromUtc: "2026-10-01T08:00:00Z"));

        sessions.InsertSession(new DialysisSessionRecord(
            SessionId: "SES-UX-NOM",
            PatientId: PatientNomId,
            PrescriptionId: "RX-UX-NOM-01",
            AssignedNurseId: "NR-MEND-01",
            SupervisingDoctorId: "DR-MEND-01",
            ChairOrBedNumber: "BED-UX-03",
            ShiftNumber: ShiftNum,
            SessionDate: ShiftDate,
            StartTimeUtc: $"{ShiftDate}T08:00:00Z",
            EndTimeUtc: null,
            SessionStatus: "InProgress",
            PreDialysisWeightKg: 66.2,
            PreSystolicBp: 128,
            PreDiastolicBp: 78,
            PreHeartRateBpm: 72,
            PreTemperatureCelsius: 36.6,
            TargetUltrafiltrationMl: 1200.0,
            PostDialysisWeightKg: null,
            PostSystolicBp: null,
            PostDiastolicBp: null,
            ActualUltrafiltrationMl: null,
            CalculatedSpKtV: null,
            CalculatedUrrPercent: null,
            SyncVersion: 1,
            LastModifiedUtc: $"{ShiftDate}T08:00:00Z"));

        sessions.AppendTelemetryTick(new IntradialyticTelemetryRecord(
            TelemetryId: 0,
            SessionId: "SES-UX-NOM",
            RecordedAtUtc: $"{ShiftDate}T09:30:00Z",
            ElapsedMinutes: 90,
            SystolicBp: 124,
            DiastolicBp: 76,
            HeartRateBpm: 74,
            BloodFlowRateMlMin: 300.0,
            DialysateFlowRateMlMin: 500.0,
            VenousPressureMmHg: 125.0,
            ArterialPressureMmHg: -135.0,
            TransmembranePressureMmHg: 75.0,
            UltrafiltrationRateMlHr: 300.0,
            CumulativeUfRemovedMl: 450.0,
            DialysateConductivityMsCm: 13.8,
            DialysateTemperatureCelsius: 36.5));
    }

    private static string FindLongitudinalMendeleyPatientId(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var patientRepo = scope.ServiceProvider.GetRequiredService<IPatientRepository>();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<ISessionRepository>();

        string bestId = PatientAlphaId;
        int bestCount = 0;

        foreach (var patient in patientRepo.GetAllPatientSummaries())
        {
            if (patient.PatientId.StartsWith("PT-UX-", StringComparison.Ordinal))
            {
                continue;
            }

            int completedCount = sessionRepo.GetRecentCompletedSessionsForPatient(patient.PatientId, 25).Count;
            if (completedCount > bestCount)
            {
                bestCount = completedCount;
                bestId = patient.PatientId;
            }
        }

        Assert(bestCount >= 6, $"Expected a longitudinal Mendeley patient with >= 6 sessions, best found had {bestCount}.");
        return bestId;
    }

    private static void SetKtVInputs(
        AnalyticsViewModel vm,
        string preBun,
        string postBun,
        string durationHrs,
        string ufLiters,
        string postWeightKg)
    {
        vm.KtVPreBun = preBun;
        vm.KtVPostBun = postBun;
        vm.KtVDurationHours = durationHrs;
        vm.KtVUfLiters = ufLiters;
        vm.KtVPostWeightKg = postWeightKg;
    }

    private static void VerifyBootstrapAutoHealCorruptedDuplicateDatabase(string mendeleyZip)
    {
        Console.WriteLine("[ITER 0/24] ClinicWorkspaceBootstrap.NeedsFreshMendeleySeed Auto-Heal (#1/#2 Duplicate Cache -> 12 Distinct Patients)...");
        string corruptDir = Path.Combine(
            Path.GetTempPath(),
            "NephroEdge_AutoHealTest_" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(corruptDir);

        try
        {
            using (var initial = new ClinicWorkspaceBootstrap(
                workspaceDirectory: corruptDir,
                startSyncHost: false,
                mendeleyZipPathOverride: mendeleyZip))
            {
                Assert(initial.InitialSeedReport is not null && initial.InitialSeedReport.ValidSessionsInserted > 0,
                    "Auto-heal test setup expected initial Mendeley seed.");
            }
            SqliteConnection.ClearAllPools();

            string sqlitePath = Path.Combine(corruptDir, "clinic_edge.sqlite");
            using (var conn = new SqliteConnection($"Data Source={sqlitePath};Pooling=False"))
            {
                conn.Open();
                using var tx = conn.BeginTransaction();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = """
                        UPDATE Patients SET FirstName = FirstName || ' #1' WHERE PatientId = (SELECT PatientId FROM Patients ORDER BY PatientId LIMIT 1 OFFSET 0);
                        UPDATE Patients SET FirstName = FirstName || ' #2' WHERE PatientId = (SELECT PatientId FROM Patients ORDER BY PatientId LIMIT 1 OFFSET 1);
                        UPDATE DialysisSessions
                        SET PatientId = 'PT-MEND-0001'
                        WHERE SessionDate = @SessionDate AND ShiftNumber = @ShiftNumber;
                        """;
                    cmd.Parameters.AddWithValue("@SessionDate", ShiftDate);
                    cmd.Parameters.AddWithValue("@ShiftNumber", ShiftNum);
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
            }
            SqliteConnection.ClearAllPools();

            MethodInfo? needsSeedMethod = typeof(ClinicWorkspaceBootstrap).GetMethod(
                "NeedsFreshMendeleySeed",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert(needsSeedMethod is not null,
                "Could not locate internal static ClinicWorkspaceBootstrap.NeedsFreshMendeleySeed(DbConnectionFactory).");

            var probeFactory = new DbConnectionFactory(sqlitePath);
            bool detectedCorrupt = (bool)needsSeedMethod!.Invoke(null, new object[] { probeFactory })!;
            SqliteConnection.ClearAllPools();
            Assert(detectedCorrupt,
                "ClinicWorkspaceBootstrap.NeedsFreshMendeleySeed must return true for corrupted #1/#2 duplicate-patient SQLite cache.");

            using (var healed = new ClinicWorkspaceBootstrap(
                workspaceDirectory: corruptDir,
                startSyncHost: false,
                mendeleyZipPathOverride: mendeleyZip))
            {
                Assert(healed.InitialSeedReport is not null && healed.InitialSeedReport.ValidSessionsInserted > 0,
                    "Auto-heal bootstrap must purge stale cache and re-seed from Mendeley zip.");

                using var scope = healed.ScopeFactory.CreateScope();
                var patientRepo = scope.ServiceProvider.GetRequiredService<IPatientRepository>();
                var sessionRepo = scope.ServiceProvider.GetRequiredService<ISessionRepository>();

                var allPatients = patientRepo.GetAllPatientSummaries();
                Assert(allPatients.Count >= 12 &&
                       allPatients.All(p => !p.FullName.Contains('#', StringComparison.Ordinal)),
                    $"Auto-healed Patients table must contain >= 12 clean names without '#', got count={allPatients.Count}.");

                var shiftBoard = sessionRepo.GetActiveShiftSessionBoard(ShiftDate, ShiftNum);
                int distinctShiftPatients = shiftBoard.Select(r => r.PatientId).Distinct(StringComparer.Ordinal).Count();
                Assert(shiftBoard.Count == 12 && distinctShiftPatients == 12 &&
                       shiftBoard.All(r => !r.PatientFullName.Contains('#', StringComparison.Ordinal)),
                    $"Auto-healed active shift must contain 12 distinct Mendeley patients without '#', got rows={shiftBoard.Count}, distinct={distinctShiftPatients}.");
            }
            SqliteConnection.ClearAllPools();

            bool stillNeedsSeed = (bool)needsSeedMethod.Invoke(null, new object[] { new DbConnectionFactory(sqlitePath) })!;
            SqliteConnection.ClearAllPools();
            Assert(!stillNeedsSeed,
                "ClinicWorkspaceBootstrap.NeedsFreshMendeleySeed must return false after auto-heal completes.");

            using (var healthyReopen = new ClinicWorkspaceBootstrap(
                workspaceDirectory: corruptDir,
                startSyncHost: false,
                mendeleyZipPathOverride: mendeleyZip))
            {
                Assert(healthyReopen.InitialSeedReport is null,
                    "Healthy auto-healed database must NOT trigger redundant reseed on subsequent startup.");
            }
            Console.WriteLine("  -> PASS: NeedsFreshMendeleySeed auto-healed corrupted #1/#2 duplicate SQLite cache to 12 distinct real Mendeley patients.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(corruptDir))
            {
                try
                {
                    Directory.Delete(corruptDir, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private static IReadOnlyList<string> CaptureWave4FocusedScreenshots(
        MainWindow window,
        MainWindowViewModel mainVm,
        IServiceScopeFactory scopeFactory,
        string longitudinalMendeleyPatientId,
        List<string> violationAccumulator)
    {
        var files = new List<string>();
        window.Width = 1366;
        window.Height = 820;
        PumpUi(window);

        var analyticsView = FindRequiredDescendant<AnalyticsView>(window);
        var safetyRxView = FindRequiredDescendant<SafetyRxWorkbenchView>(window);
        var syncMonitorView = FindRequiredDescendant<SyncMonitorView>(window);

        // 1. Analytics Tab 2 (Sessions & Kt/V) @ 1366x820
        mainVm.BannerVm.SelectPatientById(longitudinalMendeleyPatientId, notifyListeners: true);
        InvokeCommandForTab(mainVm, WorkspaceTab.PatientAnalytics);
        InvokeCommand(mainVm.AnalyticsVm.SelectSessionsTabCommand);
        mainVm.AnalyticsVm.IsKtvDrawerOpen = true;
        mainVm.AnalyticsVm.IsDryWeightLogOpen = false;
        ResetAllScrollViewersToHome(window);
        PumpUi(window);
        violationAccumulator.AddRange(
            CrossScreenGeometryAuditor.CollectWindowGeometryViolations(window, "1366x820:AnalyticsSessionsTab"));
        files.Add(SaveWindowPng(window, "02d_patient_analytics_sessions_1366x820.png"));
        mainVm.AnalyticsVm.IsKtvDrawerOpen = false;
        InvokeCommand(mainVm.AnalyticsVm.SelectLabsTabCommand);

        // 2. SafetyRx Primary Above-the-Fold @ 1366x820 + Expanded Drawers
        mainVm.BannerVm.SelectPatientById(PatientAlphaId, notifyListeners: true);
        InvokeCommandForTab(mainVm, WorkspaceTab.SafetyRxWorkbench);
        var medExpander = FindRequired<Expander>(safetyRxView, "MedicationAnemiaExpander");
        var reasoningExpander = FindRequired<Expander>(safetyRxView, "ClinicalReasoningExpander");
        medExpander.IsExpanded = false;
        reasoningExpander.IsExpanded = false;
        ResetAllScrollViewersToHome(window);
        PumpUi(window);

        var signRxBtn = FindRequired<Button>(safetyRxView, "SignPrescriptionButton");
        CrossScreenGeometryAuditor.AuditAboveTheFold(
            signRxBtn,
            window,
            maxTop: 740.0,
            contextTag: "1366x820:SafetyRx.SignPrescriptionButton",
            maxBottom: 790.0);
        files.Add(SaveWindowPng(window, "03a_safety_rx_primary_1366x820.png"));

        medExpander.IsExpanded = true;
        reasoningExpander.IsExpanded = true;
        PumpUi(window);
        FindRequired<Border>(safetyRxView, "Criterion4Card").BringIntoView();
        PumpUi(window);
        files.Add(SaveWindowPng(window, "03b_safety_rx_expanded_1366x820.png"));
        medExpander.IsExpanded = false;
        reasoningExpander.IsExpanded = false;
        ResetAllScrollViewersToHome(window);

        // 3. SyncMonitor 409 Conflict State & Expanded Technical Drawer @ 1366x820
        InvokeCommandForTab(mainVm, WorkspaceTab.SyncMonitor);
        string conflictHandshakeId;
        using (var scope = scopeFactory.CreateScope())
        {
            var handshakeService = scope.ServiceProvider.GetRequiredService<IOrderHandshakeService>();
            var created = handshakeService.QueueDirective(new Services_Unit.Core.Contracts.ClosedLoopDirectiveRequest(
                PatientId: PatientNomId,
                SessionId: null,
                PrescriptionId: null,
                DirectiveType: "DRY_WEIGHT_UPDATE",
                DirectiveSummary: "Bedside tablet mismatch awaiting clinician confirmation",
                IsmpFormattedDoseText: "64.8 kg",
                CreatedByDoctorId: "DR-MEND-01",
                ExpectedSyncVersion: 1));
            conflictHandshakeId = created.HandshakeId;
            handshakeService.AcknowledgeDirective(conflictHandshakeId, "NR-MEND-01", reportedSyncVersion: 999);
        }

        mainVm.SyncMonitorVm.RefreshDiagnostics();
        mainVm.SyncMonitorVm.FocusDirective(conflictHandshakeId);
        ResetAllScrollViewersToHome(window);
        PumpUi(window);
        violationAccumulator.AddRange(
            CrossScreenGeometryAuditor.CollectWindowGeometryViolations(window, "1366x820:SyncMonitorConflict"));
        CrossScreenGeometryAuditor.AuditNoRedOnTechnicalSync(syncMonitorView, "1366x820:SyncMonitorConflict");
        files.Add(SaveWindowPng(window, "04b_sync_monitor_conflict_1366x820.png"));

        mainVm.SyncMonitorVm.ClinicianStaffId = "DR-MEND-01";
        mainVm.SyncMonitorVm.ResolutionNotes = "Snapshot verification conflict resolved cleanly.";
        InvokeCommand(mainVm.SyncMonitorVm.ResolveConflictCommand);
        PumpUi(window);

        var techExpander = FindRequired<Expander>(syncMonitorView, "TechnicalSyncExpander");
        techExpander.IsExpanded = true;
        PumpUi(window);
        techExpander.BringIntoView();
        PumpUi(window);
        violationAccumulator.AddRange(
            CrossScreenGeometryAuditor.CollectWindowGeometryViolations(window, "1366x820:SyncMonitorTechnical"));
        CrossScreenGeometryAuditor.AuditNoRedOnTechnicalSync(syncMonitorView, "1366x820:SyncMonitorTechnical");
        files.Add(SaveWindowPng(window, "04c_sync_monitor_technical_1366x820.png"));
        techExpander.IsExpanded = false;

        // Restore clean Alpha / ShiftGrid state
        mainVm.BannerVm.SelectPatientById(PatientAlphaId, notifyListeners: true);
        mainVm.ShiftGridVm.SetRiskFilterCommand.Execute("All");
        mainVm.ShiftGridVm.FilterText = string.Empty;
        InvokeCommandForTab(mainVm, WorkspaceTab.ShiftGrid);
        ResetAllScrollViewersToHome(window);
        PumpUi(window);

        return files;
    }

    private static string SaveWindowPng(Window window, string fileName)
    {
        using var frame = window.CaptureRenderedFrame();
        Assert(frame is not null && frame.PixelSize.Width > 0 && frame.PixelSize.Height > 0,
            $"CaptureRenderedFrame returned empty frame for '{fileName}'.");
        string path = Path.Combine(ScreenshotDirectory, fileName);
        frame!.Save(path);
        return path;
    }

    private static void InvokeCommandForTab(MainWindowViewModel mainVm, WorkspaceTab tab)
    {
        switch (tab)
        {
            case WorkspaceTab.ShiftGrid:
                InvokeCommand(mainVm.NavigateShiftGridCommand);
                break;
            case WorkspaceTab.PatientAnalytics:
                InvokeCommand(mainVm.NavigateAnalyticsCommand);
                break;
            case WorkspaceTab.SafetyRxWorkbench:
                InvokeCommand(mainVm.NavigateSafetyRxCommand);
                break;
            case WorkspaceTab.SyncMonitor:
                InvokeCommand(mainVm.NavigateSyncMonitorCommand);
                break;
        }
    }

    private static void InvokeCommand(System.Windows.Input.ICommand command, object? parameter = null)
    {
        if (command.CanExecute(parameter))
        {
            command.Execute(parameter);
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

    private static string ResolveMendeleyZip()
    {
        string[] candidates =
        {
            @"D:\Study\Programming\Projects\Grad_project\knowledge-base\datasets\dataset_11.csv.zip",
            @"D:\Study\Programming\Projects\Grad_project\backend\Tests_Unit\Test_Data\7kmtsmsgfw-1\dataset_11.csv.zip"
        };

        foreach (string path in candidates)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException("Mendeley dataset_11.csv.zip not found in expected workspace locations.");
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"[ASSERTION FAILED] {message}");
        }
    }
}
