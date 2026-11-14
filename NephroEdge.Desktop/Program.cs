using System;
using Avalonia;

namespace NephroEdge.Desktop;

/// <summary>
/// Application entry point for the NephroEdge Doctor Workstation Avalonia desktop executable.
/// </summary>
public sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
