// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
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
        [InlineData(nameof(HttpRequestHeader.Authorization), "https://appservice.core.azurewebsites.net", "https://testsite.azurewebsites.net")]
        [InlineData(nameof(HttpRequestHeader.Authorization), "https://appservice.core.azurewebsites.net", "https://testsite.azurewebsites.net/azurefunctions")]
        [InlineData(nameof(HttpRequestHeader.Authorization), "https://testsite.scm.azurewebsites.net", "https://testsite.azurewebsites.net")]
        [InlineData(nameof(HttpRequestHeader.Authorization), "https://testsite.scm.azurewebsites.net", "https://testsite.azurewebsites.net/azurefunctions")]
        [InlineData(nameof(HttpRequestHeader.Authorization), "https://testsite.azurewebsites.net", "https://testsite.azurewebsites.net")]
        [InlineData(ScriptConstants.SiteTokenHeaderName)]
        [InlineData(ScriptConstants.SiteTokenHeaderName, "https://appservice.core.azurewebsites.net", "https://testsite.azurewebsites.net")]
        [InlineData(ScriptConstants.SiteTokenHeaderName, "https://AppService.Core.Azurewebsites.net", "https://TestSite.Azurewebsites.net")]
        [InlineData(ScriptConstants.SiteTokenHeaderName, "https://appservice.core.azurewebsites.net", "https://testsite.azurewebsites.net/azurefunctions")]
        [InlineData(ScriptConstants.SiteTokenHeaderName, "https://testsite.scm.azurewebsites.net", "https://testsite.azurewebsites.net")]
        [InlineData(ScriptConstants.SiteTokenHeaderName, "https://testsite.scm.azurewebsites.net", "https://testsite.azurewebsites.net/azurefunctions")]
        [InlineData(ScriptConstants.SiteTokenHeaderName, "https://testsite.azurewebsites.net", "https://testsite.azurewebsites.net")]
        public async Task InvokeAdminApi_ValidToken_Succeeds(string headerName, string issuer = null, string audience = null)
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
        public async Task InvokeAdminApi_InvalidAudience_Fails(string headerName)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "admin/host/status");
            string token = _fixture.Host.GenerateAdminJwtToken(audience: "invalid");

            if (string.Compare(nameof(HttpRequestHeader.Authorization), headerName) == 0)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            else
            {
                request.Headers.Add(headerName, token);
            }

            _fixture.Host.ClearLogMessages();

            var response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

            var validationError = _fixture.Host.GetScriptHostLogMessages().Single(p => p.Level == LogLevel.Error);
            Assert.Equal(ScriptConstants.LogCategoryHostAuthentication, validationError.Category);
            Assert.Equal("Token audience validation failed for audience 'invalid'.", validationError.FormattedMessage);
            Assert.True(validationError.Exception.Message.StartsWith("IDX10231: Audience validation failed."));
        }

        [Theory]
        [InlineData(nameof(HttpRequestHeader.Authorization))]
        [InlineData(ScriptConstants.SiteTokenHeaderName)]
        public async Task InvokeAdminApi_InvalidIssuer_Fails(string headerName)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "admin/host/status");
            string token = _fixture.Host.GenerateAdminJwtToken(issuer: "invalid");

            if (string.Compare(nameof(HttpRequestHeader.Authorization), headerName) == 0)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            else
            {
                request.Headers.Add(headerName, token);
            }

            _fixture.Host.ClearLogMessages();

            var response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

            var validationError = _fixture.Host.GetScriptHostLogMessages().Single(p => p.Level == LogLevel.Error);
            Assert.Equal(ScriptConstants.LogCategoryHostAuthentication, validationError.Category);
            Assert.Equal("Token issuer validation failed for issuer 'invalid'.", validationError.FormattedMessage);
            Assert.Equal("IDX10205: Issuer validation failed.", validationError.Exception.Message);
        }

        [Theory]
        [InlineData(nameof(HttpRequestHeader.Authorization))]
        [InlineData(ScriptConstants.SiteTokenHeaderName)]
        public async Task InvokeAdminApi_InvalidSignature_Fails(string headerName)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "admin/host/status");

            byte[] keyBytes = TestHelpers.GenerateKeyBytes();
            string token = _fixture.Host.GenerateAdminJwtToken(key: keyBytes);

            if (string.Compare(nameof(HttpRequestHeader.Authorization), headerName) == 0)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            else
            {
                request.Headers.Add(headerName, token);
            }

            _fixture.Host.ClearLogMessages();

            var response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

            var validationError = _fixture.Host.GetScriptHostLogMessages().Single(p => p.Level == LogLevel.Error);
            Assert.Equal(ScriptConstants.LogCategoryHostAuthentication, validationError.Category);
            Assert.Equal("Token validation failed.", validationError.FormattedMessage);
            Assert.True(validationError.Exception.Message.StartsWith("IDX10503: Signature validation failed."));
        }

        [Fact]
        public async Task InvokeAdminApi_ValidToken_UTF8Encoding_Succeeds()
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "admin/host/status");
            string key = SecretsUtility.GetEncryptionKeyValue();
            string token = _fixture.Host.GenerateAdminJwtToken(key: Encoding.UTF8.GetBytes(key));
            request.Headers.Add(ScriptConstants.SiteTokenHeaderName, token);

            var response = await _fixture.Host.HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
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
