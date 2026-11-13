using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using NephroEdge.Desktop.Bootstrap;
using NephroEdge.Desktop.ViewModels;

namespace NephroEdge.Desktop;

/// <summary>
/// Avalonia Application entry handling MedTechTheme loading, ClassicDesktop workspace bootstrap,
/// clean socket/timer teardown, and deterministic --smoke-exit-ms automated startup smoke runs.
/// </summary>
public partial class App : Application
{
    private ClinicWorkspaceBootstrap? _bootstrap;
    private DispatcherTimer? _smokeExitTimer;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            string[] args = desktop.Args ?? Array.Empty<string>();
            int? smokeExitMs = ParseIntArg(args, "--smoke-exit-ms");
            string? workspaceDir = ParseStringArg(args, "--workspace-dir");
            bool disableSyncHost = HasFlag(args, "--no-sync-host");

            _bootstrap = new ClinicWorkspaceBootstrap(
                workspaceDirectory: workspaceDir,
                startSyncHost: !disableSyncHost);

            var mainVm = new MainWindowViewModel(
                _bootstrap.ScopeFactory,
                () => _bootstrap.SyncHost,
                _bootstrap.RestartSyncHostAsync);

            var mainWindow = new MainWindow
            {
                DataContext = mainVm
            };

            desktop.MainWindow = mainWindow;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.Exit += OnDesktopExit;

            if (smokeExitMs.HasValue && smokeExitMs.Value > 0)
            {
                _smokeExitTimer = new DispatcherTimer(
                    TimeSpan.FromMilliseconds(smokeExitMs.Value),
                    DispatcherPriority.Background,
                    (_, _) =>
                    {
                        _smokeExitTimer?.Stop();
                        _smokeExitTimer = null;
                        mainWindow.Close();
                        desktop.Shutdown(0);
                    });
                _smokeExitTimer.Start();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        _smokeExitTimer?.Stop();
        _smokeExitTimer = null;

        if (sender is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.DataContext is IDisposable vmDisposable)
        {
            vmDisposable.Dispose();
        }

        _bootstrap?.Dispose();
        _bootstrap = null;
    }

    private static bool HasFlag(string[] args, string flagName)
    {
        foreach (string arg in args)
        {
            if (string.Equals(arg, flagName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ParseStringArg(string[] args, string optionName)
    {
        string prefix = optionName + "=";
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string val = arg[prefix.Length..].Trim();
                return string.IsNullOrWhiteSpace(val) ? null : val;
            }

            if (string.Equals(arg, optionName, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                string val = args[i + 1].Trim();
                return string.IsNullOrWhiteSpace(val) ? null : val;
            }
        }

        return null;
    }

    private static int? ParseIntArg(string[] args, string optionName)
    {
        string? raw = ParseStringArg(args, optionName);
        if (raw is not null && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            return parsed;
        }

        return null;
    }
}
