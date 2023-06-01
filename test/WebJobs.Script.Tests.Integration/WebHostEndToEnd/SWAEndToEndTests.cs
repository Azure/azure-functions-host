// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd
{
    /// <summary>
    /// Tests verifying the Static Web Apps deployment configuration.
    /// </summary>
    public class SWAEndToEndTests : IClassFixture<SWAEndToEndTests.TestFixture>
    {
        private TestFixture _fixture;

        public SWAEndToEndTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void NoStorageConfigured_SecretsDisabled()
        {
            Assert.Null(Environment.GetEnvironmentVariable("AzureWebJobsStorage"));
            Assert.False(_fixture.Host.SecretManagerProvider.SecretsEnabled);
        }

        [Fact]
        public async Task InvokeFunction_AnonymousLevel_Succeeds()
        {
            var code = "test";

            // send along a code query param, and ensure that the invocation succeeds
            // the token isn't interpreted as a function key in this case since keys are disabled
            var content = new StringContent(JsonConvert.SerializeObject(new { scenario = "staticWebApp" }));
            var response = await _fixture.Host.HttpClient.PostAsync($"api/HttpTrigger-Scenarios?code={code}", content);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            Assert.Equal(code, responseBody);
        }

        [Fact]
        public async Task InvokeFunction_FunctionLevel_NoAuthToken_Fails()
        {
            // the function declares an auth level of Function, so invocations should fail
            var response = await _fixture.Host.HttpClient.GetAsync("api/HttpTrigger-FunctionAuth?code=test");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

            response = await _fixture.Host.HttpClient.GetAsync("api/HttpTrigger-FunctionAuth");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory]
        [InlineData(nameof(HttpRequestHeader.Authorization))]
        [InlineData(ScriptConstants.SiteTokenHeaderName)]
        public async Task InvokeFunction_FunctionLevel_ValidToken_Succeeds(string headerName)
        {
            // if an admin token is passed, the function invocation succeeds
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "api/HttpTrigger-FunctionAuth?code=test");
            string token = GetSWAAdminJwtToken();

            if (string.Compare(nameof(HttpRequestHeader.Authorization), headerName) == 0)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            else
            {
                request.Headers.Add(headerName, token);
            }

            var response = await _fixture.Host.HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        private string GetSWAAdminJwtToken()
        {
            // Ensure we use AzureWebEncryptionKey to generate tokens, as that's what SWA does
            string keyValue = _fixture.SWAEncryptionKey;
            byte[] keyBytes = keyValue.ToKeyBytes();
            string token = _fixture.Host.GenerateAdminJwtToken(key: keyBytes);

            return token;
        }

        [Fact]
        public async Task SyncTriggers_Succeeds()
        {
            _fixture.Host.ClearLogMessages();

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "admin/host/synctriggers");
            string token = _fixture.Host.GenerateAdminJwtToken();
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var response = await _fixture.Host.HttpClient.SendAsync(request);

            // the sync request will fail because we're not running in Antares and can't
            // communicate with the FE, but it suffices to verify that we attempted to send the request
            var logs = _fixture.Host.GetScriptHostLogMessages().Where(p => p.Category == typeof(FunctionsSyncManager).FullName).ToArray();
            var log = logs[0].FormattedMessage;
            Assert.True(log.StartsWith("Making SyncTriggers request"));
            Assert.True(log.Contains("HttpTrigger-FunctionAuth"));
            Assert.True(log.Contains("HttpTrigger-Scenarios"));
        }

        public class TestFixture : EndToEndTestFixture
        {
            private TestScopedEnvironmentVariable _scopedEnvironment;

            public TestFixture() : base(@"TestScripts\CSharp", "csharp", RpcWorkerConstants.DotNetLanguageWorkerName, addTestSettings: false)
            {
                // SWA generates their own key and sets via AzureWebEncryptionKey
                // This should take precedence over the default key
                var testKeyBytes = TestHelpers.GenerateKeyBytes();
                var testKey = TestHelpers.GenerateKeyHexString(testKeyBytes);
                SWAEncryptionKey = testKey;

                // Default key provisioned by Antares and available via WEBSITE_AUTH_ENCRYPTION_KEY
                var defaultTestKeyBytes = TestHelpers.GenerateKeyBytes();
                var defaultTestKey = TestHelpers.GenerateKeyHexString(defaultTestKeyBytes);

                var settings = new Dictionary<string, string>()
                {
                    { "AzureWebEncryptionKey", testKey },
                    { EnvironmentSettingNames.WebSiteAuthEncryptionKey, defaultTestKey },
                    { "AzureWebJobsStorage", null },
                    { EnvironmentSettingNames.AzureWebsiteName, "testsite" }
                };
                _scopedEnvironment = new TestScopedEnvironmentVariable(settings);
            }

            public string SWAEncryptionKey { get; }

            public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
            {
                base.ConfigureScriptHost(webJobsBuilder);

                webJobsBuilder.Services.Configure<ScriptJobHostOptions>(o =>
                {
                    o.Functions = new[]
                    {
                        "HttpTrigger-Scenarios",
                        "HttpTrigger-FunctionAuth"
                    };
                });
            }

            public override void ConfigureScriptHost(IServiceCollection services)
            {
                base.ConfigureScriptHost(services);

                // replace the base mock FunctionsSyncManager
                var service = services.FirstOrDefault(d => d.ServiceType == typeof(IFunctionsSyncManager));
                services.Remove(service);
                services.AddSingleton<IFunctionsSyncManager, FunctionsSyncManager>();
            }

            public override void ConfigureWebHost(IServiceCollection services)
            {
                base.ConfigureWebHost(services);

                // replace the base mock ISecretManagerProvider
                var service = services.FirstOrDefault(d => d.ServiceType == typeof(ISecretManagerProvider));
                services.Remove(service);
                services.TryAddSingleton<ISecretManagerProvider, DefaultSecretManagerProvider>();
            }

            public override async Task DisposeAsync()
            {
                await base.DisposeAsync();

                _scopedEnvironment.Dispose();
            }
        }
    }

}
