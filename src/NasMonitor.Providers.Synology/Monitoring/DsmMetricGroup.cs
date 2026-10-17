using System.Text.Json;
using NasMonitor.Core.Monitoring;

namespace NasMonitor.Providers.Synology.Monitoring;

internal sealed record DsmMetricGroup(JsonElement? Data, MetricAvailability Availability, DateTimeOffset CollectedAt);
