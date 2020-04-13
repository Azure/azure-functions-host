using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace WebJobs.Script.Tests.Perf.Dashboard
{
    public class DevOpsClient
    {
        private ILogger _logger;
        private string _organization = "azfunc";
        private string _project = "Azure Functions";

        public DevOpsClient(ILogger logger)
        {
            _logger = logger;
        }

        public async Task<ArtifactResult> GetArtifacts(string branch, string accessToken)
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));

                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(
                        ASCIIEncoding.ASCII.GetBytes(
                            string.Format("{0}:{1}", "", accessToken))));

                string minDate = DateTime.Now.AddDays(-7).ToString("yyyy-MM-dd");
                using (HttpResponseMessage response = await client.GetAsync($"https://dev.azure.com/{_organization}/{_project}/_apis/build/builds?api-version=5.1&definitions=37&minTime={minDate}&branchName={branch}"))
                {
                    response.EnsureSuccessStatusCode();
                    var builds = await response.Content.ReadAsAsync<Builds>();

                    foreach (var build in builds.Value.Where(x => x.Status.ToLower() == "completed").OrderByDescending(x => x.Id))
                    {
                        using (HttpResponseMessage artifactResponse = await client.GetAsync($"https://dev.azure.com/{_organization}/{_project}/_apis/build/builds/{build.Id}/artifacts?api-version=4.1"))
                        {
                            artifactResponse.EnsureSuccessStatusCode();
                            var artifacts = await artifactResponse.Content.ReadAsAsync<Artifacts>();

                            var appArtifact = artifacts.Value.FirstOrDefault(x => x.Resource.Url.Contains("WebJobs.Script.Performance.App-ci"));
                            var extensionArtifact = artifacts.Value.FirstOrDefault(x => x.Resource.Url.Contains("ci.win-x32.inproc"));
                            if (appArtifact != null && extensionArtifact != null)
                            {
                                return new ArtifactResult()
                                {
                                    ExtensionUrl = extensionArtifact.Resource.DownloadUrl.Replace("format=zip", $"format=file&subPath=%2FFunctions.Private.{build.BuildNumber}-ci.win-x32.inproc.zip"),
                                    AppUrl = appArtifact.Resource.DownloadUrl.Replace("format=zip", $"format=file&subPath=%2FWebJobs.Script.Performance.App.1.0.0.nupkg"),
                                };
                            }
                        }
                    }
                }
                return null;
            }

        }
    }

    public class Builds
    {
        public Build[] Value { get; set; }
    }
    
    public class Build
    {
        public int Id { get; set; }
        public string Status { get; set; }
        public string BuildNumber { get; set; }
    }

    public class Artifacts
    {
        public Artifact[] Value { get; set; }
    }

   public class Artifact
   {
        public ArtifactResource Resource { get; set; } 
   }

    public class ArtifactResource
    {
        public string Url { get; set; }
        public string DownloadUrl { get; set; }
    }

    public class ArtifactResult
    {
        public string ExtensionUrl { get; set; }
        public string AppUrl { get; set; }
    }
}
