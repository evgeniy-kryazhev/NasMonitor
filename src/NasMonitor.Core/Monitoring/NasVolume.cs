namespace NasMonitor.Core.Monitoring;

/// <summary>A logical volume; aggregate storage uses sum(used) / sum(total), not average percentages.</summary>
public sealed record NasVolume(string Id, double UsedBytes, double TotalBytes);
