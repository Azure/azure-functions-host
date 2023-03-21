// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd
{
    /// <summary>
    /// Tests for our JWT token auth handler.
    /// </summary>
    public class JwtTokenAuthTests : IClassFixture<JwtTokenAuthTests.TestFixture>
    {
        private TestFixture _fixture;

        public JwtTokenAuthTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Theory]
        [InlineData(nameof(HttpRequestHeader.Authorization))]
        [InlineData(nameof(HttpRequestHeader.Authorization), ScriptConstants.AdminJwtAppServiceIssuer, ScriptConstants.AdminJwtAppServiceIssuer)]
        [InlineData(ScriptConstants.SiteTokenHeaderName)]
        [InlineData(ScriptConstants.SiteTokenHeaderName, ScriptConstants.AdminJwtAppServiceIssuer, ScriptConstants.AdminJwtAppServiceIssuer)]
        public async Task InvokeAdminApi_ValidToken_Succeeds(string headerName, string audience = null, string issuer = null)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "admin/host/status");
            string token = _fixture.Host.GenerateAdminJwtToken(audience, issuer);

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

        [Theory]
        [InlineData(nameof(HttpRequestHeader.Authorization))]
        [InlineData(ScriptConstants.SiteTokenHeaderName)]
        public async Task InvokeAdminApi_InvalidToken_Fails(string headerName)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "admin/host/status");
            string token = _fixture.Host.GenerateAdminJwtToken("invalid", "invalid");

            if (string.Compare(nameof(HttpRequestHeader.Authorization), headerName) == 0)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            else
            {
                request.Headers.Add(headerName, token);
            }

            var response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        public class TestFixture : EndToEndTestFixture
        {
            private TestScopedEnvironmentVariable _scopedEnvironment;

            public TestFixture() : base(@"TestScripts\CSharp", "csharp", RpcWorkerConstants.DotNetLanguageWorkerName, addTestSettings: false)
            {
                var testKeyBytes = TestHelpers.GenerateKeyBytes();
                var testKey = TestHelpers.GenerateKeyHexString(testKeyBytes);

                var settings = new Dictionary<string, string>()
                {
                    { "AzureWebEncryptionKey", testKey },
                    { EnvironmentSettingNames.WebSiteAuthEncryptionKey, testKey },
                    { "AzureWebJobsStorage", null },
                    { EnvironmentSettingNames.AzureWebsiteName, "testsite" }
                };
                _scopedEnvironment = new TestScopedEnvironmentVariable(settings);
            }

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
