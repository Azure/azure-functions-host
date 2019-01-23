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
    public class AppVeyorClient : IDisposable
    {
        private HttpClient _client;
        private ILogger _logger;
        private readonly string _accountName;
        private bool _disposed = false;

        public AppVeyorClient(ILogger logger)
        {
            _client = new HttpClient
            {
                BaseAddress = new Uri("https://ci.appveyor.com/api/")
            };
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Environment.GetEnvironmentVariable("AppVeyorToken"));
            _logger = logger;            
            _accountName = Environment.GetEnvironmentVariable("AccountName", EnvironmentVariableTarget.Process);
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _client.Dispose();
                _disposed = true;
            }
        }

        public async Task<string> GetLastSuccessfulBuildVersionAsync(string branch, string projectSlug)
        {
            string startBuildId = null;
            string buildVersion = null;

            while (true)
            {
                var url = $"projects/{_accountName}/{projectSlug}/history?recordsNumber=50&branch={branch}";
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

        public async Task<string> GetArtifactUrlAsync(string buildVersion, string projectSlug, string jobName, string artifactSubString)
        {
            string jobId = null;
            string artifactUrl = null;

            if (buildVersion != null)
            {
                var response = await _client.GetAsync($"projects/{_accountName}/{projectSlug}/build/{buildVersion}");
                response.EnsureSuccessStatusCode();
                var buildDetails = await response.Content.ReadAsAsync<AppVeyorBuildDetails>();
                jobId = buildDetails.Build.Jobs.First(x => x.Name == jobName).JobId;
            }

            if (jobId != null)
            {
                var response = await _client.GetAsync($"https://ci.appveyor.com/api/buildjobs/{jobId}/artifacts");
                response.EnsureSuccessStatusCode();
                var artifacts = await response.Content.ReadAsAsync<AppVeyorArtifact[]>();

                var artifact = artifacts.FirstOrDefault(p => p.FileName.Contains(artifactSubString));
                artifactUrl = artifact == null ? string.Empty : $"https://ci.appveyor.com/api/buildjobs/{jobId}/artifacts/{artifact.FileName}";

                _logger.LogInformation($"Found artifact: '{artifactUrl}'");
            }

            return artifactUrl;
        }

        public async Task UpdateEnvironmentVariableAsync(string projectSlug, string siteExtensionUrl)
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

        public async Task StartBuildAsync(string branch, string projectSlug)
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
