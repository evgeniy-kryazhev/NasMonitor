using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using NasMonitor.Core.Monitoring;

namespace NasMonitor.Providers.Synology.Monitoring;

/// <summary>Maps version-checked DSM fields to explicit units, never inferring missing values.</summary>
internal static partial class DsmSnapshotParser
{
    internal static NasSnapshot Parse(Guid nasId,
        DsmMetricGroup utilization,
        DsmMetricGroup system,
        DsmMetricGroup storage)
    {
        JsonElement? cpuData = Child(utilization.Data, "cpu");
        double? cpu = Number(cpuData, "user_load") + Number(cpuData, "system_load") + Number(cpuData, "other_load");
        if (new[] { "user_load", "system_load", "other_load" }.Any(name => Number(cpuData, name) is < 0 or > 100))
        {
            cpu = double.NaN;
        }

        NasMetric cpuMetric = Metric(cpu, utilization);
        NasMetric ramMetric = Metric(Number(Child(utilization.Data, "memory"), "real_usage"), utilization);
        JsonElement? totalNetwork = null;
        JsonElement? network = Child(utilization.Data, "network");
        if (network is { ValueKind: JsonValueKind.Array })
        {
            foreach (JsonElement entry in network.Value.EnumerateArray())
            {
                if (Text(entry, "device") == "total")
                {
                    totalNetwork = entry;
                    break;
                }
            }
        }

        // DSM already reports rates in bytes/sec. Summing total + interfaces would double count bonds.
        NasMetric download = Metric(Number(totalNetwork, "rx"), utilization, maximum: double.MaxValue);
        NasMetric upload = Metric(Number(totalNetwork, "tx"), utilization, maximum: double.MaxValue);
        NasMetric temperature = Metric(Number(system.Data, "sys_temp"), system, -50, 150);
        NasMetric uptime = Metric(UptimeSeconds(Child(system.Data, "up_time")), system, maximum: 315_576_000);
        (NasMetric storageMetric, IReadOnlyList<NasVolume> volumes) = ReadVolumes(storage);
        return new NasSnapshot(nasId,
            utilization.CollectedAt,
            cpuMetric,
            ramMetric,
            storageMetric,
            download,
            upload,
            temperature,
            uptime,
            Text(system.Data, "model"),
            Text(system.Data, "firmware_ver"),
            volumes);
    }

    private static (NasMetric, IReadOnlyList<NasVolume>) ReadVolumes(DsmMetricGroup group)
    {
        JsonElement? array = Child(group.Data, "volumes");
        var volumes = new List<NasVolume>();
        bool invalid = false;
        if (array is { ValueKind: JsonValueKind.Array })
        {
            foreach (JsonElement volume in array.Value.EnumerateArray())
            {
                string? id = Text(volume, "id");
                JsonElement? size = Child(volume, "size");
                double? used = Number(size, "used");
                double? total = Number(size, "total");
                if (id is null || volumes.Any(existing => existing.Id == id) ||
                    used is null || total is null || !double.IsFinite(used.Value) || !double.IsFinite(total.Value) ||
                    total <= 0 || used < 0 || used > total)
                {
                    invalid = true;
                    continue;
                }

                volumes.Add(new NasVolume(id, used.Value, total.Value));
            }
        }

        double? percentage = volumes.Count == 0
            ? null
            : volumes.Sum(volume => volume.UsedBytes) /
            volumes.Sum(volume => volume.TotalBytes) * 100;
        return (Metric(invalid ? double.NaN : percentage, group), volumes);
    }

    private static NasMetric Metric(double? value, DsmMetricGroup group, double minimum = 0, double maximum = 100)
    {
        if (group.Availability != MetricAvailability.Available)
        {
            return new NasMetric(null, group.CollectedAt, group.Availability);
        }

        return NasMetric.Create(value, group.CollectedAt, minimum, maximum);
    }

    private static JsonElement? Child(JsonElement? parent, string name)
    {
        return parent is { ValueKind: JsonValueKind.Object } && parent.Value.TryGetProperty(name, out JsonElement value)
            ? value
            : null;
    }

    private static double? Number(JsonElement? parent, string name)
    {
        JsonElement? value = Child(parent, name);
        if (value is null || value.Value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.Value.ValueKind == JsonValueKind.Number && value.Value.TryGetDouble(out double number))
        {
            return number;
        }

        if (value.Value.ValueKind == JsonValueKind.String && double.TryParse(value.Value.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out number))
        {
            return number;
        }

        return double.NaN;
    }

    private static string? Text(JsonElement? parent, string name)
    {
        JsonElement? value = Child(parent, name);
        if (value is not { ValueKind: JsonValueKind.String })
        {
            return null;
        }

        string text = value.Value.GetString() ?? "";
        return text[..Math.Min(128, text.Length)];
    }

    private static double? UptimeSeconds(JsonElement? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Value.ValueKind != JsonValueKind.String)
        {
            return double.NaN;
        }

        string text = value.Value.GetString() ?? "";
        Match match = UptimePattern().Match(text);
        if (!match.Success || !double.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out double hours) ||
            !int.TryParse(match.Groups[2].Value, out int minutes) ||
            !int.TryParse(match.Groups[3].Value, out int seconds) ||
            minutes > 59 || seconds > 59)
        {
            return double.NaN;
        }

        return hours * 3600 + minutes * 60 + seconds;
    }

    [GeneratedRegex(@"^(\d{1,8}):(\d{2}):(\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex UptimePattern();
}
