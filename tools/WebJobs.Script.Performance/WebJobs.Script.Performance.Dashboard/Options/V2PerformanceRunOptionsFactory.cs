using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace WebJobs.Script.Tests.Perf.Dashboard.Options
{
    public class V2PerformanceRunOptionsFactory : IPerformanceRunOptionsFactory
    {
        private const string Branch = "refs/heads/v2.x";
        private ILogger _log;

        public V2PerformanceRunOptionsFactory(ILogger log)
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
