using System;

namespace NephroEdge.Desktop.HeadlessTests;

/// <summary>
/// Entry point executing all 24 automated Avalonia Headless MedTech UX,
/// cross-screen geometry, ISMP typography, IEC 60601-1-8 alarm, and sync bridge iterations.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            DesktopUxIterationTests.RunAllIterations();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[HEADLESS UX SUITE FAILED] {ex}");
            return 1;
        }
    }
}
