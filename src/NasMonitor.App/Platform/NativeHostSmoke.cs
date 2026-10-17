using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace NasMonitor.App.Platform;

/// <summary>Opt-in native AppKit round trip on a real Avalonia NSWindow, on the main UI thread.</summary>
internal static class NativeHostSmoke
{
    public static Window CreateWindow(IClassicDesktopStyleApplicationLifetime lifetime, string reportPath)
    {
        var window = new Window
        {
            Title = "NAS Monitor native API smoke test",
            Width = 300,
            Height = 200,
            ShowInTaskbar = false,
            Content = new TextBlock { Text = "Native API smoke test; not desktop acceptance." }
        };
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() => Run(window, lifetime, reportPath));
        return window;
    }

    private static void Run(Window window, IClassicDesktopStyleApplicationLifetime lifetime, string reportPath)
    {
        var passed = false;
        string? error = null;
        MacWindowState? original = null, attached = null, restored = null;
        AppKitDesktopApi? api = null;
        IntPtr handle = IntPtr.Zero;
        try
        {
            if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("AppKit smoke requires macOS.");
            var native = window.TryGetPlatformHandle();
            if (native is null || native.HandleDescriptor != "NSWindow" || native.Handle == IntPtr.Zero)
                throw new InvalidOperationException("Avalonia did not expose a live NSWindow.");
            handle = native.Handle;
            api = new AppKitDesktopApi();
            original = api.Capture(handle);
            try
            {
                api.Apply(handle);
                api.Apply(handle);
                attached = api.Capture(handle);
                if (attached.Value.Level != AppKitDesktopApi.DesktopLevel
                    || attached.Value.CollectionBehavior != AppKitDesktopApi.DesktopBehavior)
                    throw new InvalidOperationException("NSWindow did not retain the requested native desktop properties.");
            }
            finally
            {
                api.Restore(handle, original.Value);
                restored = api.Capture(handle);
            }
            if (restored != original) throw new InvalidOperationException("NSWindow properties did not restore exactly.");
            passed = true;
        }
        catch (Exception e) { error = $"{e.GetType().Name}: {e.Message}"; }
        finally
        {
            try
            {
                var report = new
                {
                    schemaVersion = 1,
                    platform = RuntimeInformation.OSDescription,
                    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                    passed,
                    scope = "AppKit native property round trip only; no desktop, Spaces or Mission Control acceptance",
                    error,
                    originalLevel = original is { } o ? (long?)o.Level : null,
                    attachedLevel = attached is { } a ? (long?)a.Level : null,
                    restoredLevel = restored is { } r ? (long?)r.Level : null,
                    originalBehavior = original is { } b ? (ulong?)b.CollectionBehavior : null,
                    attachedBehavior = attached is { } c ? (ulong?)c.CollectionBehavior : null,
                    restoredBehavior = restored is { } d ? (ulong?)d.CollectionBehavior : null
                };
                var fullPath = Path.GetFullPath(reportPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllText(fullPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception e) { Console.Error.WriteLine($"Could not save native smoke report: {e.Message}"); passed = false; }
            window.Close();
            lifetime.Shutdown(passed ? 0 : 1);
        }
    }
}
