using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace WebJobs.Script.Tests.Perf.Dashboard.Options
{
    public class V3PerformanceRunOptionsFactory : IPerformanceRunOptionsFactory
    {
        private const string Branch = "refs/heads/dev";
        private ILogger _log;

        public V3PerformanceRunOptionsFactory(ILogger log)
        {
            _log = log;
        }

        public async Task<PerformanceRunOptions> CreateAsync()
        {
            var options = new PerformanceRunOptions();

            var devOpsClient = new DevOpsClient(_log);
            var artifactResult = await devOpsClient.GetArtifacts(Branch, options.DevOpsAccessToken);
            options.ExtensionUrl = artifactResult.ExtensionUrl;
            options.AppUrl = artifactResult.AppUrl;

            return options;
        }
    }
}
