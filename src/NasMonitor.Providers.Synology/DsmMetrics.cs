using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using NasMonitor.Core;

namespace NasMonitor.Providers.Synology;

public static class DsmMetrics
{
    public static JsonElement Property(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var child) ? child : default;
    private static string Text(JsonElement value, string name) => Property(value, name).ToString();
    private static double? Number(JsonElement value) => double.TryParse(value.ToString(), NumberStyles.Float,
        CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) && number >= 0 ? number : null;
    private static long? Bytes(JsonElement value) => long.TryParse(value.ToString(), NumberStyles.Integer,
        CultureInfo.InvariantCulture, out var number) && number >= 0 ? number : null;
    private static double? Percent(double? value) => value is >= 0 and <= 100 ? value : null;
    private static IEnumerable<JsonElement> Items(JsonElement value) => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];
    public static NasSnapshot Parse(JsonElement utilization, JsonElement volumes, JsonElement disks, IEnumerable<string> warnings)
    {
        var cpu = Property(utilization, "cpu");
        var user = Number(Property(cpu, "user_load"));
        var system = Number(Property(cpu, "system_load"));
        var load = Percent(user + system + (Number(Property(cpu, "other_load")) ?? 0));
        var ram = Percent(Number(Property(Property(utilization, "memory"), "real_usage")));
        var notes = warnings.ToList();
        if (load is null || ram is null) notes.Add("Some CPU/RAM metrics are unavailable.");
        return new(DateTimeOffset.UtcNow, load, ram,
            Items(Property(utilization, "network")).Select(x => new NetworkMetric(Text(x, "device"), Number(Property(x, "rx")), Number(Property(x, "tx")))).ToImmutableArray(),
            Items(volumes).Select(x => new VolumeMetric(Text(x, "name") is { Length: > 0 } name ? name : Text(x, "id"),
                Bytes(Property(Property(x, "size"), "used")) ?? Bytes(Property(x, "size_used")),
                Bytes(Property(Property(x, "size"), "total")) ?? Bytes(Property(x, "size_total")), Text(x, "status"))).ToImmutableArray(),
            Items(disks).Select(x => new DiskMetric(Text(x, "name") is { Length: > 0 } name ? name : Text(x, "id"),
                Text(x, "model"), Text(x, "status"), Number(Property(x, "temp")))).ToImmutableArray(), notes.ToImmutableArray());
    }
}
