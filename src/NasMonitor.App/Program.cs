using Avalonia;

namespace NasMonitor.App;

internal static class Program
{
    internal static string? NativeSmokeReport { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--native-host-smoke"))
        {
            if (!OperatingSystem.IsMacOS() || args.Length != 2 || args[0] != "--native-host-smoke"
                || string.IsNullOrWhiteSpace(args[1]))
            {
                Console.Error.WriteLine("Usage on macOS: NasMonitor.App --native-host-smoke <report.json>");
                return 2;
            }
            NativeSmokeReport = args[1];
        }
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
