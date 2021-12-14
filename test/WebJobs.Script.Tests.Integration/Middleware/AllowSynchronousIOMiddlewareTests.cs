using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Middleware
{
    public class AllowSynchronousIOMiddlewareTests
    {
        [Fact(Skip = "Seems very difficult to trigger this failure now.")]
        public async Task SyncRead_Fails_ByDefault()
        {
            using (var host = GetHost())
            {
                HostSecretsInfo secrets = await host.SecretManager.GetHostSecretsAsync();
                var response = await MakeRequest(host.HttpClient, secrets.MasterKey);

                Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

                var a = host.GetScriptHostLogMessages().Select(p => p.Exception?.ToString());

                Assert.Contains(host.GetScriptHostLogMessages(), p => p.Exception != null && p.Exception.ToString().Contains("Synchronous operations are disallowed."));
            }
        }

        [Fact]
        public async Task SyncRead_Succeeds_WithFlag()
        {
            using (var host = GetHost(d => d.Add(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagAllowSynchronousIO)))
            {
                HostSecretsInfo secrets = await host.SecretManager.GetHostSecretsAsync();
                var response = await MakeRequest(host.HttpClient, secrets.MasterKey);

                response.EnsureSuccessStatusCode();
            }
        }

        [Fact]
        public async Task SyncRead_Succeeds_InV2CompatMode()
        {
            using (var host = GetHost(d => d.Add(EnvironmentSettingNames.FunctionsV2CompatibilityModeKey, "true")))
            {
                HostSecretsInfo secrets = await host.SecretManager.GetHostSecretsAsync();
                var response = await MakeRequest(host.HttpClient, secrets.MasterKey);

                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
            }
        }

        [Fact]
        public async Task SyncDownload_Succeeds()
        {
            using (var host = GetHost())
            {
                HostSecretsInfo secrets = await host.SecretManager.GetHostSecretsAsync();
                var response = await MakeDownloadRequest(host.HttpClient, secrets.MasterKey);

                response.EnsureSuccessStatusCode();
                Assert.True(response.Content.Headers.ContentLength > 0);
            }
        }

        private static TestFunctionHost GetHost(Action<IDictionary<string, string>> addEnvironmentVariables = null)
        {
            string scriptPath = @"TestScripts\DirectLoad\";
            string logPath = Path.Combine(Path.GetTempPath(), @"Functions");

            var host = new TestFunctionHost(scriptPath, logPath,
                configureWebHostServices: s =>
                {
                    IDictionary<string, string> dict = new Dictionary<string, string>();
                    addEnvironmentVariables?.Invoke(dict);
                    s.AddSingleton<IEnvironment>(_ => new TestEnvironment(dict));
                });

            return host;
        }

        private static Task<HttpResponseMessage> MakeDownloadRequest(HttpClient client, string masterKey)
        {
            var request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format($"http://localhost/admin/functions/download?code={masterKey}")),
                Method = HttpMethod.Get
            };

            return client.SendAsync(request);
        }

        private static Task<HttpResponseMessage> MakeRequest(HttpClient client, string masterKey)
        {
            var request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format($"http://localhost/api/function1?code={masterKey}&name=brett")),
                Method = HttpMethod.Post
            };

            var input = new JObject
            {
                { "scenario", "syncRead" }
            };

            request.Content = new StringContent(input.ToString(), Encoding.UTF8, "application/json");

            return client.SendAsync(request);
        }
    }
}
