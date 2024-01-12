using Microsoft.Extensions.Options;
using OpenTelemetry.Logs;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    internal class OpenTelemetryLogEnrichmentProcessor(IOptions<ScriptJobHostOptions> hostOptions) : OpenTelemetryBaseEnrichmentProcessor<LogRecord>(hostOptions)
    {
        protected override void AddHostInstanceId(LogRecord data, string hostInstanceId)
        {
            var newAttributes = new List<KeyValuePair<string, object>>(data.Attributes ?? Array.Empty<KeyValuePair<string, object>>())
            {
                new(ScriptConstants.LogPropertyHostInstanceIdKey, hostInstanceId)
            };
            data.Attributes = newAttributes;
        }

        protected override void AddProcessId(LogRecord data)
        {
            bool hasEventName = data.Attributes.Any(data => data.Key == ScriptConstants.LogPropertyEventNameKey);
            var newAttributes = new List<KeyValuePair<string, object>>(data.Attributes ?? Array.Empty<KeyValuePair<string, object>>())
            {
                new(ScriptConstants.LogPropertyProcessIdKey, System.Diagnostics.Process.GetCurrentProcess().Id),
                new("Telemetry type", "logg")
            };
            data.Attributes = newAttributes;
        }
    }
}
