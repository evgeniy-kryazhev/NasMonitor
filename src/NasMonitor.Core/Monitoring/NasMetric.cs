namespace NasMonitor.Core.Monitoring;

/// <summary>A numeric metric with its own collection time and availability; units belong to the snapshot field.</summary>
public sealed record NasMetric(double? Value, DateTimeOffset CollectedAt, MetricAvailability Availability)
{
    /// <summary>Optional numeric DSM error for this metric, without raw response data or credentials.</summary>
    public int? DsmCode { get; init; }

    /// <summary>Accept only finite values in the specified inclusive range (percentages by default).</summary>
    public static NasMetric Create(double? value, DateTimeOffset collectedAt, double minimum = 0, double maximum = 100)
    {
        if (value is null)
        {
            return new NasMetric(null, collectedAt, MetricAvailability.Unavailable);
        }

        if (!double.IsFinite(value.Value) || value < minimum || value > maximum)
        {
            return new NasMetric(null, collectedAt, MetricAvailability.Invalid);
        }

        return new NasMetric(value, collectedAt, MetricAvailability.Available);
    }
}
