using Microsoft.Extensions.Options;
using OpenTelemetry;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    internal abstract class OpenTelemetryBaseEnrichmentProcessor<T> : BaseProcessor<T>
    {
        private readonly ScriptJobHostOptions _hostOptions;

        // Category
        // LogLevel

        public OpenTelemetryBaseEnrichmentProcessor(IOptions<ScriptJobHostOptions> hostOptions)
        {
            this._hostOptions = hostOptions.Value;
        }

        protected abstract void AddHostInstanceId(T data, string hostInstanceId);
        protected abstract void AddProcessId(T data);

        sealed public override void OnEnd(T data)
        {
            AddHostInstanceId(data, _hostOptions.InstanceId);
            AddProcessId(data);

            base.OnEnd(data);
        }
    }
}
