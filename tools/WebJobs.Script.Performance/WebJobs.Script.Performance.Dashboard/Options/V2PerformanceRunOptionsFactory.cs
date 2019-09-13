using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace WebJobs.Script.Tests.Perf.Dashboard.Options
{
    public class V2PerformanceRunOptionsFactory : IPerformanceRunOptionsFactory
    {
        private const string Branch = "dev";
        private ILogger _log;

        public V2PerformanceRunOptionsFactory(ILogger log)
        {
            _log = log;
        }

        public async Task<PerformanceRunOptions> CreateAsync()
        {
            var options = new PerformanceRunOptions();

            using (var appVeyorClient = new AppVeyorClient(_log))
            {
                // Get latest private extension url from appvayor build
                string lastSuccessfulVersion = await appVeyorClient.GetLastSuccessfulBuildVersionAsync(Branch, options.FunctionsHostSlug);
                options.ExtensionUrl = await appVeyorClient.GetArtifactUrlAsync(lastSuccessfulVersion, options.FunctionsHostSlug, "Image: Visual Studio 2017", "inproc");
                options.AppUrl = await appVeyorClient.GetArtifactUrlAsync(lastSuccessfulVersion, options.FunctionsHostSlug, "Image: Visual Studio 2017", "WebJobs.Script.Performance.App");
            }

            return options;
        }
    }
}
