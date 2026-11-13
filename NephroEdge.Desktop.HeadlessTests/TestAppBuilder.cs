using Avalonia;
using Avalonia.Headless;

namespace NephroEdge.Desktop.HeadlessTests;

/// <summary>
/// Headless Skia-backed Avalonia AppBuilder enabling real pixel framebuffer capture
/// via CaptureRenderedFrame() without requiring a physical desktop window server.
/// </summary>
public sealed class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false
            })
            .WithInterFont()
            .LogToTrace();
    }
}
