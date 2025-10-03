// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.HealthChecks
{
    public partial class TelemetryHealthCheckPublisher : IHealthCheckPublisher
    {
        private readonly HealthCheckMetrics _metrics;
        private readonly TelemetryHealthCheckPublisherOptions _options;
        private readonly ILogger _logger;

        public TelemetryHealthCheckPublisher(
            HealthCheckMetrics metrics,
            TelemetryHealthCheckPublisherOptions options,
            ILogger<TelemetryHealthCheckPublisher> logger)
        {
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets the tag for this health check publisher. For unit test purposes only.
        /// </summary>
        internal string Tag => _options.Tag;

        public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(report);

            if (_options.Tag is { Length: > 0 } tag)
            {
                report = report.Filter(FilterForTag);
            }

            if (report.Entries.Count == 0)
            {
                // No entries to report, so we can skip logging and metrics.
                return Task.CompletedTask;
            }

            tag = _options.Tag ?? string.Empty; // for logs/metrics later on.
            if (report.Status == HealthStatus.Healthy)
            {
                if (!_options.LogOnlyUnhealthy)
                {
                    Log.Healthy(_logger, tag, report.Status);
                }
            }
            else
            {
                foreach (var entry in report.Entries)
                {
                    if (entry.Value.Status != HealthStatus.Healthy)
                    {
                        _metrics.UnhealthyHealthCheck.Record(entry.Key, entry.Value, tag);
                    }
                }

                Log.Unhealthy(_logger, tag, report.Status, new HealthEntries(report));
            }

            _metrics.HealthCheckReport.Record(report, tag);
            return Task.CompletedTask;
        }

        private bool FilterForTag(string name, HealthReportEntry entry)
        {
            return entry.Tags.Contains(_options.Tag);
        }

        /// <summary>
        /// Wraps the health report entries for logging purposes.
        /// </summary>
        /// <param name="report">The report to log.</param>
        /// <remarks>
        /// By wrapping in a struct, we delay performing JSON serialization
        /// until this is actually being logged (and not when it is filtered out).
        /// </remarks>
        private readonly struct HealthEntries(HealthReport report)
        {
            private static readonly JsonSerializerOptions _options
                = new() { Converters = { new HealthEntriesConverter() } };

            public override string ToString()
            {
                // We use a JSON serializer here to ensure proper escaping of values.
                return JsonSerializer.Serialize(report, _options);
            }
        }

        private class HealthEntriesConverter : JsonConverter<HealthReport>
        {
            public override HealthReport Read(
                ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                throw new NotImplementedException();
            }

            public override void Write(
                Utf8JsonWriter writer, HealthReport value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();

                foreach ((string name, HealthReportEntry entry) in value.Entries)
                {
                    writer.WritePropertyName(name);
                    writer.WriteStartObject();
                    writer.WriteString("status", entry.Status.ToString());
                    writer.WriteString("description", entry.Description);
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }
        }

        private static partial class Log
        {
            [LoggerMessage(0, LogLevel.Warning, "[Tag='{Tag}'] Process reporting unhealthy: {Status}. Health check entries are {Entries}")]
            public static partial void Unhealthy(
                ILogger logger,
                string tag,
                HealthStatus status,
                HealthEntries entries);

            [LoggerMessage(1, LogLevel.Debug, "[Tag='{Tag}'] Process reporting healthy: {Status}.")]
            public static partial void Healthy(
                ILogger logger,
                string tag,
                HealthStatus status);
        }
    }

    public class TelemetryHealthCheckPublisherOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether to log for only non-healthy values or not. Default <c>true</c>.
        /// </summary>
        public bool LogOnlyUnhealthy { get; set; } = true;

        /// <summary>
        /// Gets or sets the tag to filter this health check for. <c>null</c> will perform no filtering.
        /// </summary>
        public string Tag { get; set; }
    }
}
