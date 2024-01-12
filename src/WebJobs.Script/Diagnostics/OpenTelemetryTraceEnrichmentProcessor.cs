using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    internal class OpenTelemetryTraceEnrichmentProcessor(IOptions<ScriptJobHostOptions> hostOptions) : OpenTelemetryBaseEnrichmentProcessor<Activity>(hostOptions)
    {
        protected override void AddHostInstanceId(Activity data, string hostInstanceId) => data.AddTag(ScriptConstants.LogPropertyHostInstanceIdKey, hostInstanceId);
        protected override void AddProcessId(Activity data)
        {
            data.AddTag(ScriptConstants.LogPropertyProcessIdKey, Process.GetCurrentProcess().Id.ToString());
            data.AddTag("Telemetry type", "trace");
        }
    }
}
