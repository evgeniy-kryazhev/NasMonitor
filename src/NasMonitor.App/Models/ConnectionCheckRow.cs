using NasMonitor.App.Localization;
using NasMonitor.Core.Connections;

namespace NasMonitor.App.Models;

/// <summary>Localized, non-secret check evidence displayed in the editor.</summary>
public sealed record ConnectionCheckRow(string Stage, string Detail)
{
    /// <summary>Render controlled stage/error identifiers through RU resources.</summary>
    public static ConnectionCheckRow From(ConnectionCheck check)
    {
        string detail = check.State == CheckState.Failed
            ? UiText.Get("Error" + check.Error)
            : UiText.Get("Check" + check.State);
        if (check.DsmCode.HasValue)
        {
            detail += " (DSM " + check.DsmCode.Value + ")";
        }

        return new ConnectionCheckRow(UiText.Get("Stage" + check.Stage), detail);
    }
}
