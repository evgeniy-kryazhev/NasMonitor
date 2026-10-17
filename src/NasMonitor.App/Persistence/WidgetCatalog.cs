namespace NasMonitor.App.Persistence;

/// <summary>Versioned, non-secret widget membership persisted independently of NAS profiles.</summary>
public sealed record WidgetCatalog(int SchemaVersion, List<WidgetCatalogEntry> Widgets, Guid? SelectedWidgetId);
