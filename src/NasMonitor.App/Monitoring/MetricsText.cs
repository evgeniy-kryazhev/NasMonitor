using NasMonitor.Core;

namespace NasMonitor.App.Monitoring;

internal static class MetricsText
{
    private static string Percent(double? value) => value is { } number ? $"{number:0.#}%" : "—";
    private static string Size(double? value)
    {
        if (value is null) return "—";
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        var number = value.Value;
        var unit = 0;
        while (number >= 1024 && unit < units.Length - 1) { number /= 1024; unit++; }
        return $"{number:0.#} {units[unit]}";
    }
    public static string Format(MonitorUpdate? update)
    {
        if (update?.Snapshot is not { } snapshot) return "No readings yet. Connect to the NAS to begin monitoring.";
        var lines = new List<string>
        {
            $"CPU {Percent(snapshot.CpuPercent)}     RAM {Percent(snapshot.RamPercent)}",
            $"Measured {snapshot.CapturedAt.ToLocalTime():HH:mm:ss}{(update.IsStale ? " · STALE — last successful reading" : "")}" 
        };
        foreach (var network in snapshot.Network)
            lines.Add($"{network.Device}: ↓ {Size(network.ReceiveBytesPerSecond)}/s   ↑ {Size(network.TransmitBytesPerSecond)}/s");
        foreach (var volume in snapshot.Volumes)
            lines.Add($"{volume.Name}: {Size(volume.UsedBytes)} / {Size(volume.TotalBytes)} · {volume.Status}");
        foreach (var disk in snapshot.Disks)
            lines.Add($"{disk.Name} {disk.Model}: {disk.Status}{(disk.Temperature is { } temp ? $" · {temp:0.#} °C" : "")}");
        return string.Join('\n', lines);
    }
    public static string State(MonitorUpdate? update) => update is null ? "Disconnected — enter credentials to connect"
        : $"{update.State}{(update.IsStale ? " · stale" : "")}: {update.Message}";
}
