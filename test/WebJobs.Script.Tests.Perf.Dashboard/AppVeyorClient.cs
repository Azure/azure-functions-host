using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace WebJobs.Script.Tests.Perf.Dashboard
{
    public class AppVeyorClient
    {
        private HttpClient _client;
        private ILogger _logger;
        private readonly string _functionsHostProjectSlug;
        private readonly string _accountName;

        public AppVeyorClient(ILogger logger)
        {
            _client = new HttpClient
            {
                BaseAddress = new Uri("https://ci.appveyor.com/api/")
            };
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Environment.GetEnvironmentVariable("AppVeyorToken"));
            _logger = logger;
            _functionsHostProjectSlug = Environment.GetEnvironmentVariable("FunctionHostProjectSlug", EnvironmentVariableTarget.Process);
            _accountName = Environment.GetEnvironmentVariable("AccountName", EnvironmentVariableTarget.Process);
        }

        public static async Task StartPerf(ILogger logger)
        {
            AppVeyorClient client = new AppVeyorClient(logger);
            await client.StartBuild(new Build()
            {
                Branch = Environment.GetEnvironmentVariable("PerformanceBranch", EnvironmentVariableTarget.Process),
                ProjectSlug = Environment.GetEnvironmentVariable("PerformanceProjectSlug", EnvironmentVariableTarget.Process),
                Artifact = "inproc",
                JobName = "Image: Visual Studio 2017"
            });
        }

        public async Task StartBuild(Build build)
        {
            string lastSuccessfulBuild = await GetLastSuccessfulBuildVersionAsync(build.Branch);

            if (lastSuccessfulBuild == null)
            {
                _logger.LogInformation("No sucessful builds found.");
                return;
            }

            string extensionUrl = await GetPrivateSiteExtensionUrlAsync(lastSuccessfulBuild, build);

            if (extensionUrl == null)
            {
                _logger.LogInformation("Could not find private site extension.");
                return;
            }

            await UpdateEnvironmentVariableAsync(build.ProjectSlug, extensionUrl);
            await StartEndToEndBuildAsync(build.ProjectSlug, build.Branch);
        }

        private async Task<string> GetLastSuccessfulBuildVersionAsync(string branch)
        {
            string startBuildId = null;
            string buildVersion = null;

            while (true)
            {
                var url = $"projects/{_accountName}/{_functionsHostProjectSlug}/history?recordsNumber=50&branch={branch}";
                if (startBuildId != null)
                {
                    url += "&startbuildId=" + startBuildId;
                }

                var response = await _client.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var history = await response.Content.ReadAsAsync<AppVeyorHistory>();

                foreach (dynamic build in history.Builds)
                {
                    if (string.Equals(build.Status, "success") &&
                        build.PullRequestId == null &&
                        build.Message != "Functions Scheduled Build")
                    {
                        buildVersion = build.Version;
                        _logger.LogInformation($"Last successful build of branch '{branch}': {buildVersion} ({build.Message})");
                        break;
                    }

                    startBuildId = build.BuildId;
                }

                if (buildVersion != null || history.Builds.Length == 0)
                {
                    break;
                }
            }

            return buildVersion;
        }

        private async Task<string> GetPrivateSiteExtensionUrlAsync(string buildVersion, Build build)
        {
            string jobId = null;
            string siteExtensionUrl = null;

            if (buildVersion != null)
            {
                var response = await _client.GetAsync($"projects/{_accountName}/{_functionsHostProjectSlug}/build/{buildVersion}");
                response.EnsureSuccessStatusCode();
                var buildDetails = await response.Content.ReadAsAsync<AppVeyorBuildDetails>();
                jobId = buildDetails.Build.Jobs.First(x => x.Name == build.JobName).JobId;
            }

            if (jobId != null)
            {
                var response = await _client.GetAsync($"https://ci.appveyor.com/api/buildjobs/{jobId}/artifacts");
                response.EnsureSuccessStatusCode();
                var artifacts = await response.Content.ReadAsAsync<AppVeyorArtifact[]>();

                string siteExtensionFileName = artifacts.Select(p => p.FileName).Single(p => p.Contains(build.Artifact));

                siteExtensionUrl = $"https://ci.appveyor.com/api/buildjobs/{jobId}/artifacts/{siteExtensionFileName}";
                _logger.LogInformation($"Found private site extension: '{siteExtensionFileName}'");
            }

            return siteExtensionUrl;
        }

        private async Task UpdateEnvironmentVariableAsync(string projectSlug, string siteExtensionUrl)
        {
            var response = await _client.GetAsync($"/api/projects/{_accountName}/{projectSlug}/settings/environment-variables");
            response.EnsureSuccessStatusCode();

            var envVars = await response.Content.ReadAsAsync<AppVeyorEnvironmentVariable[]>();

            var packageVar = envVars.SingleOrDefault(p => p.Name == "AzureWebjobsRuntimePrivateExtensionPackageUrl");

            if (packageVar != null)
            {
                packageVar.Value.Value = siteExtensionUrl;
            }
            else
            {
                throw new InvalidOperationException("Environment variable not found.");
            }


            _logger.LogInformation($"Updating AppVeyor build '{projectSlug}' environment variable...");
            response = await _client.PutAsync($"/api/projects/{_accountName}/{projectSlug}/settings/environment-variables", 
                new StringContent(JsonConvert.SerializeObject(envVars), Encoding.UTF8, "application/json"));

            response.EnsureSuccessStatusCode();
            _logger.LogInformation("Done.");
        }

        private async Task StartEndToEndBuildAsync(string projectSlug, string branch)
        {
            _logger.LogInformation($"Starting new build on {projectSlug} {branch}...");

            var response = await _client.PostAsync("builds",
                new StringContent(JsonConvert.SerializeObject(new
                {
                    accountName = _accountName,
                    projectSlug = projectSlug,
                    branch = branch
                }), Encoding.UTF8, "application/json"));
            _logger.LogInformation(await response.Content.ReadAsStringAsync());
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Done.");
        }
    }

    public class AppVeyorBuild
    {
        public string Status { get; set; }
        public string PullRequestId { get; set; }
        public string Version { get; set; }
        public string Message { get; set; }
        public string BuildId { get; set; }
        public AppVeyorJob[] Jobs { get; set; }
    }

    public class AppVeyorHistory
    {
        public AppVeyorBuild[] Builds { get; set; }
    }

    public class AppVeyorBuildDetails
    {
        public AppVeyorBuild Build { get; set; }
    }

    public class AppVeyorJob
    {
        public string JobId { get; set; }

        public string Name { get; set; }
    }

    public class AppVeyorArtifact
    {
        public string FileName { get; set; }
    }

    public class AppVeyorEnvironmentVariable
    {
        public string Name { get; set; }
        public AppVeyorEnvironmentVariableValue Value { get; set; }
    }

    public class AppVeyorEnvironmentVariableValue
    {
        public bool IsEncrypted { get; set; }
        public string Value { get; set; }
    }

    public class Build
    {
        public string Branch { get; set; }
        public string ProjectSlug { get; set; }
        public string Artifact { get; set; }
        public string JobName { get; set; }
    }
}
