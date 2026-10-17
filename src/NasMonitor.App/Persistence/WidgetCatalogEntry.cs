namespace NasMonitor.App.Persistence;

/// <summary>Stable widget identity and compatibility with the original settings directory.</summary>
public sealed record WidgetCatalogEntry(Guid Id, bool UsesLegacySettings = false);
