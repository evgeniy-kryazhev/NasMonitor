using System.Runtime.InteropServices;
using NasMonitor.Core.Widgets;

namespace NasMonitor.App.Services;

public sealed partial class WidgetController
{
    private bool contentMinimumHeightVerified;
    internal bool IsSmokeHealthy => tick > 0 && IsHostAlive() && widget?.IsVisible == true && widget.Opacity > 0;

    /// <summary>Machine-readable smoke evidence; API success is not visual acceptance.</summary>
    public object GetSmokeReport()
    {
        return new
        {
            OperatingSystem = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            SyntheticUpdates = tick,
            ContentMinimumHeightVerified = contentMinimumHeightVerified,
            NativeHostCode = lastHostCode,
            NativeHostAlive = IsHostAlive(),
            WidgetVisible = widget?.IsVisible == true,
            VisualDesktopAcceptance = "Unverified"
        };
    }

    internal async Task VerifyContentMinimumHeightAsync()
    {
        WidgetAppearance originalAppearance = viewModel.CurrentAppearance;
        WidgetLayout originalLayout = layout;
        try
        {
            layout = layout with { Height = 72 };
            viewModel.ApplyAppearance(new WidgetAppearance());
            Restore();
            await Task.Delay(200);
            double expandedHeight = widget?.Height ?? 0;
            double enlargedHeight = Math.Min(600, expandedHeight + 80);
            layout = layout with { Height = enlargedHeight };
            Restore();
            await Task.Delay(200);
            if (Math.Abs((widget?.Height ?? 0) - enlargedHeight) > 1)
            {
                throw new InvalidOperationException("widget.manual-height-not-preserved");
            }

            layout = layout with { Height = 72 };
            Restore();
            await Task.Delay(200);
            if (Math.Abs((widget?.Height ?? 0) - expandedHeight) > 1)
            {
                throw new InvalidOperationException("widget.content-minimum-not-enforced");
            }

            viewModel.ApplyAppearance(new WidgetAppearance(ShowCpu: false,
                ShowRam: false,
                ShowLastUpdate: false,
                ShowStorage: false,
                ShowNetwork: false,
                ShowTemperature: false,
                ShowUptime: false));
            layout = layout with { Height = 72 };
            Restore();
            await Task.Delay(200);
            double compactHeight = widget?.Height ?? 0;
            if (expandedHeight < compactHeight + 100 || compactHeight > 150 || compactHeight < 72)
            {
                throw new InvalidOperationException("widget.flexible-height-failed");
            }

            contentMinimumHeightVerified = true;
        }
        finally
        {
            viewModel.ApplyAppearance(originalAppearance);
            layout = originalLayout;
            Restore();
        }
    }
}
