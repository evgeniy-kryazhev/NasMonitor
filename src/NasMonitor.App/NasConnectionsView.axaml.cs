using Avalonia.Controls;

namespace NasMonitor.App;

/// <summary>DSM connection page hosted by the main window.</summary>
public partial class NasConnectionsView : UserControl
{
    /// <summary>Create XAML controls for the connection editor.</summary>
    public NasConnectionsView()
    {
        InitializeComponent();
    }

    internal void VerifyPageLayout()
    {
        if (Math.Abs(PageContent.Bounds.X - 28) > 0.5 ||
            Math.Abs(FormScroll.Bounds.Width - PageContent.Bounds.Width) > 0.5 ||
            Math.Abs(PageContent.Bounds.Right - (Bounds.Width - 28)) > 0.5)
        {
            throw new InvalidOperationException("connections.page-layout-failed");
        }

        if (Math.Abs(AddNasButton.Bounds.Right - PageContent.Bounds.Width) > 0.5 ||
            Math.Abs(AddNasButton.Bounds.Bottom - PageContent.Bounds.Height) > 0.5 ||
            FormScroll.Bounds.Bottom > AddNasButton.Bounds.Top)
        {
            throw new InvalidOperationException("connections.fixed-footer-layout-failed");
        }
    }

}
