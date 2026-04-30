// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Description;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Properties;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd
{
    /// <summary>
    /// End-to-end tests covering the ARM exposure controls applied to extension webhook
    /// routes (<c>runtime/webhooks/{extensionName}</c>).
    /// </summary>
    /// <remarks>
    /// Three test extensions are registered to cover the three classes of behavior:
    /// <list type="bullet">
    /// <item><description>An extension marked with <see cref="AllowArmWebhookAccessAttribute"/> — ARM-bridged requests are forwarded.</description></item>
    /// <item><description>An extension with no opt-in — ARM-bridged requests are blocked unconditionally, regardless of the auth level on the request.</description></item>
    /// <item><description>An extension whose name matches an entry in the exclusion list — ARM-bridged requests are forwarded even without the opt-in attribute.</description></item>
    /// </list>
    /// </remarks>
    [Trait(TestTraits.Group, TestTraits.NonE2EWebHost)]
    public class ExtensionWebHookArmTests : IClassFixture<ExtensionWebHookArmTests.TestFixture>
    {
        private const string OptedInExtensionName = "armtestoptedin";
        private const string OptedOutExtensionName = "armtestoptedout";
        private const string ExcludedExtensionName = "textextension";

        private readonly TestFixture _fixture;

        public ExtensionWebHookArmTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        // ----- Opted-out extension (no [AllowArmWebhookAccess]) -----
        // ARM-bridged requests are blocked regardless of authorization level.

        [Fact]
        public async Task OptedOut_ArmGet_AdminJwt_Blocked()
        {
            HttpRequestMessage request = CreateExtensionRequest(HttpMethod.Get, OptedOutExtensionName, includeArmHeaders: true, adminJwt: _fixture.Host.GenerateAdminJwtToken());
            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);

            await AssertArmBlockedAsync(response, OptedOutExtensionName);
        }

        [Fact]
        public async Task OptedOut_ArmPost_AdminJwt_Blocked()
        {
            HttpRequestMessage request = CreateExtensionRequest(HttpMethod.Post, OptedOutExtensionName, includeArmHeaders: true, adminJwt: _fixture.Host.GenerateAdminJwtToken());
            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);

            await AssertArmBlockedAsync(response, OptedOutExtensionName);
        }

        [Fact]
        public async Task OptedOut_ArmGet_SystemKey_Blocked()
        {
            // Even a valid system key does not allow an ARM-bridged request through to a
            // non-opted-in extension. The host cannot guarantee the extension follows ARM
            // RBAC rules (e.g. not returning secrets over GET), so it doesn't forward.
            string systemKey = await GetExtensionSystemKeyAsync(OptedOutExtensionName);
            HttpRequestMessage request = CreateExtensionRequest(HttpMethod.Get, OptedOutExtensionName, includeArmHeaders: true, systemKey: systemKey);
            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);

            await AssertArmBlockedAsync(response, OptedOutExtensionName);
        }

        [Fact]
        public async Task OptedOut_ArmGet_OwnerCoAdmin_Blocked()
        {
            // The owner / co-admin (legacy) bypass that applies to the existing
            // [ResourceContainsSecrets] check does NOT apply to extension webhook routes.
            HttpRequestMessage request = CreateExtensionRequest(
                HttpMethod.Get,
                OptedOutExtensionName,
                includeArmHeaders: true,
                adminJwt: _fixture.Host.GenerateAdminJwtToken(),
                additionalHeaders: new Dictionary<string, string>
                {
                    { ScriptConstants.AntaresClientAuthorizationSourceHeader, "Legacy" }
                });

            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);

            await AssertArmBlockedAsync(response, OptedOutExtensionName);
        }

        [Fact]
        public async Task OptedOut_Direct_SystemKey_Allowed()
        {
            string systemKey = await GetExtensionSystemKeyAsync(OptedOutExtensionName);
            HttpRequestMessage request = CreateExtensionRequest(HttpMethod.Get, OptedOutExtensionName, includeArmHeaders: false, systemKey: systemKey);
            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);

            await AssertHandlerInvokedAsync(response, OptedOutExtensionName, "GET", expectedIsArmRequest: false);
        }

        [Fact]
        public async Task OptedOut_Direct_AdminJwt_Allowed()
        {
            // Admin tokens that don't go over the ARM bridge are allowed straight through.
            HttpRequestMessage request = CreateExtensionRequest(HttpMethod.Get, OptedOutExtensionName, includeArmHeaders: false, adminJwt: _fixture.Host.GenerateAdminJwtToken());
            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);

            await AssertHandlerInvokedAsync(response, OptedOutExtensionName, "GET", expectedIsArmRequest: false);
        }

        [Fact]
        public async Task OptedOut_Direct_NoAuth_Unauthorized()
        {
            HttpRequestMessage request = CreateExtensionRequest(HttpMethod.Get, OptedOutExtensionName, includeArmHeaders: false);
            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // ----- Opted-in extension ([AllowArmWebhookAccess] applied) -----
        // ARM-bridged requests are forwarded; the extension owns RBAC enforcement.

        [Fact]
        public async Task OptedIn_ArmGet_AdminJwt_Allowed()
        {
            HttpRequestMessage request = CreateExtensionRequest(HttpMethod.Get, OptedInExtensionName, includeArmHeaders: true, adminJwt: _fixture.Host.GenerateAdminJwtToken());
            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);

            await AssertHandlerInvokedAsync(response, OptedInExtensionName, "GET", expectedIsArmRequest: true);
        }

        [Fact]
        public async Task OptedIn_ArmPost_AdminJwt_Allowed()
        {
            HttpRequestMessage request = CreateExtensionRequest(HttpMethod.Post, OptedInExtensionName, includeArmHeaders: true, adminJwt: _fixture.Host.GenerateAdminJwtToken());
            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);

            await AssertHandlerInvokedAsync(response, OptedInExtensionName, "POST", expectedIsArmRequest: true);
        }

        [Fact]
        public async Task OptedIn_Direct_SystemKey_Allowed()
        {
            string systemKey = await GetExtensionSystemKeyAsync(OptedInExtensionName);
            HttpRequestMessage request = CreateExtensionRequest(HttpMethod.Get, OptedInExtensionName, includeArmHeaders: false, systemKey: systemKey);
            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);

            await AssertHandlerInvokedAsync(response, OptedInExtensionName, "GET", expectedIsArmRequest: false);
        }

        [Fact]
        public async Task OptedIn_Direct_AdminJwt_Allowed()
        {
            // Mirrors OptedOut_Direct_AdminJwt_Allowed: the new opt-in gate must not interfere
            // with non-ARM admin-JWT requests to an opted-in extension.
            HttpRequestMessage request = CreateExtensionRequest(HttpMethod.Get, OptedInExtensionName, includeArmHeaders: false, adminJwt: _fixture.Host.GenerateAdminJwtToken());
            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);

            await AssertHandlerInvokedAsync(response, OptedInExtensionName, "GET", expectedIsArmRequest: false);
        }

        // ----- Extension in the exclusion list -----
        // Treated like an opted-in extension by the filter, even without the attribute.

        [Fact]
        public async Task Excluded_ArmGet_AdminJwt_Allowed()
        {
            HttpRequestMessage request = CreateExtensionRequest(HttpMethod.Get, ExcludedExtensionName, includeArmHeaders: true, adminJwt: _fixture.Host.GenerateAdminJwtToken());
            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);

            await AssertHandlerInvokedAsync(response, ExcludedExtensionName, "GET", expectedIsArmRequest: true);
        }

        [Fact]
        public async Task Excluded_ArmPost_AdminJwt_Allowed()
        {
            HttpRequestMessage request = CreateExtensionRequest(HttpMethod.Post, ExcludedExtensionName, includeArmHeaders: true, adminJwt: _fixture.Host.GenerateAdminJwtToken());
            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);

            await AssertHandlerInvokedAsync(response, ExcludedExtensionName, "POST", expectedIsArmRequest: true);
        }

        // ----- Rollout / rollback gating -----
        // The new default-deny behavior is gated by the ArmWebhookOptInEnforcement hosting
        // config. When the value is cleared, enforcement is disabled (rollback) and
        // ARM-bridged requests reach opted-out extensions just like before.

        [Fact]
        public async Task EnforcementDisabled_OptedOut_ArmGet_AdminJwt_Allowed()
        {
            using (WithArmWebhookOptInConfig(null))
            {
                HttpRequestMessage request = CreateExtensionRequest(HttpMethod.Get, OptedOutExtensionName, includeArmHeaders: true, adminJwt: _fixture.Host.GenerateAdminJwtToken());
                HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);

                await AssertHandlerInvokedAsync(response, OptedOutExtensionName, "GET", expectedIsArmRequest: true);
            }
        }

        [Fact]
        public async Task EnforcementEnabledNoExclusions_ExcludedName_ArmGet_AdminJwt_Blocked()
        {
            // A single "|" enables enforcement with an empty exclusion list, so an extension
            // that previously relied on the default exclusion entry (and is not opted in
            // via [AllowArmWebhookAccess]) is now blocked.
            using (WithArmWebhookOptInConfig("|"))
            {
                HttpRequestMessage request = CreateExtensionRequest(HttpMethod.Get, ExcludedExtensionName, includeArmHeaders: true, adminJwt: _fixture.Host.GenerateAdminJwtToken());
                HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);

                await AssertArmBlockedAsync(response, ExcludedExtensionName);
            }
        }

        // ----- Helpers -----

        private static async Task AssertArmBlockedAsync(HttpResponseMessage response, string expectedExtensionName)
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            string content = await response.Content.ReadAsStringAsync();
            Assert.Equal(
                string.Format(CultureInfo.InvariantCulture, Resources.ArmRequestNotAllowedForExtension, expectedExtensionName),
                content);
        }

        private static async Task AssertHandlerInvokedAsync(HttpResponseMessage response, string expectedExtensionName, string expectedMethod, bool expectedIsArmRequest)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            ExtensionInvocationInfo info = await response.Content.ReadFromJsonAsync<ExtensionInvocationInfo>();
            Assert.NotNull(info);
            Assert.Equal(expectedExtensionName, info.ExtensionName);
            Assert.Equal(expectedMethod, info.HttpMethod);
            Assert.Equal(expectedIsArmRequest, info.IsArmRequest);
        }

        // Temporarily overrides the ArmWebhookOptInEnforcement hosting config feature for the
        // duration of the returned scope, restoring the original value (including absence)
        // on disposal. The options instance is a singleton on the WebHost container, so
        // mutations are visible to the running filter on subsequent requests.
        private IDisposable WithArmWebhookOptInConfig(string value)
        {
            FunctionsHostingConfigOptions options = _fixture.Host.WebHostServices
                .GetRequiredService<IOptionsMonitor<FunctionsHostingConfigOptions>>().CurrentValue;
            string original = options.ArmWebhookOptInEnforcement;
            options.ArmWebhookOptInEnforcement = value;
            return new ConfigScope(() => options.ArmWebhookOptInEnforcement = original);
        }

        private sealed class ConfigScope : IDisposable
        {
            private readonly Action _onDispose;

            public ConfigScope(Action onDispose)
            {
                _onDispose = onDispose;
            }

            public void Dispose() => _onDispose();
        }

        private async Task<string> GetExtensionSystemKeyAsync(string extensionName)
        {
            HostSecretsInfo secrets = await _fixture.Host.SecretManager.GetHostSecretsAsync();
            string keyName = $"{extensionName}_extension";

            // The DefaultScriptWebHookProvider creates the extension system key on demand the
            // first time GetUrl is called for the extension. With TestSecretManager the value
            // is then available via GetHostSecretsAsync.
            Assert.True(
                secrets.SystemKeys.TryGetValue(keyName, out string keyValue),
                $"Expected system key '{keyName}' to have been generated for extension '{extensionName}'.");
            return keyValue;
        }

        private static HttpRequestMessage CreateExtensionRequest(
            HttpMethod method,
            string extensionName,
            bool includeArmHeaders,
            string systemKey = null,
            string adminJwt = null,
            IDictionary<string, string> additionalHeaders = null)
        {
            HttpRequestMessage request = new HttpRequestMessage(method, $"runtime/webhooks/{extensionName}");

            if (includeArmHeaders)
            {
                request.Headers.Add(ScriptConstants.AntaresARMRequestTrackingIdHeader, "1234");
                request.Headers.Add(ScriptConstants.AntaresARMExtensionsRouteHeader, "1");
            }

            if (!string.IsNullOrEmpty(systemKey))
            {
                request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, systemKey);
            }

            if (!string.IsNullOrEmpty(adminJwt))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            }

            if (additionalHeaders is not null)
            {
                foreach (var (name, value) in additionalHeaders)
                {
                    request.Headers.Add(name, value);
                }
            }

            return request;
        }

        public sealed class ExtensionInvocationInfo
        {
            public string ExtensionName { get; set; }

            public string HttpMethod { get; set; }

            public bool IsArmRequest { get; set; }
        }

        public class TestFixture : EndToEndTestFixture
        {
            private TestScopedEnvironmentVariable _scopedEnvironment;

            public TestFixture()
                : base(@"TestScripts\CSharp", "armwebhooks", RpcWorkerConstants.DotNetLanguageWorkerName, addTestSettings: false)
            {
                byte[] testKeyBytes = TestHelpers.GenerateKeyBytes();
                string testKey = TestHelpers.GenerateKeyHexString(testKeyBytes);

                Dictionary<string, string> settings = new()
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

                webJobsBuilder.AddExtension<OptedInExtensionConfigProvider>();
                webJobsBuilder.AddExtension<OptedOutExtensionConfigProvider>();
                webJobsBuilder.AddExtension<ExcludedExtensionConfigProvider>();

                webJobsBuilder.Services.Configure<ScriptJobHostOptions>(o =>
                {
                    o.Functions = Array.Empty<string>();
                });
            }

            public override void ConfigureWebHost(IServiceCollection services)
            {
                base.ConfigureWebHost(services);

                // Enable ARM webhook opt-in enforcement with the default exclusion list
                // ("textextension"). Individual tests can override this value at runtime via the
                // WithArmWebhookOptInConfig helper to exercise rollback / no-exclusion modes.
                services.Configure<FunctionsHostingConfigOptions>(o =>
                {
                    o.ArmWebhookOptInEnforcement = ExcludedExtensionName;
                });
            }

            public override async Task DisposeAsync()
            {
                await base.DisposeAsync();
                _scopedEnvironment.Dispose();
            }
        }

        [Extension("ArmTestOptedIn", configurationSection: "armtestoptedin")]
        [AllowArmWebhookAccess]
        private sealed class OptedInExtensionConfigProvider : TestWebHookExtensionBase
        {
            public OptedInExtensionConfigProvider()
                : base(OptedInExtensionName)
            {
            }
        }

        [Extension("ArmTestOptedOut", configurationSection: "armtestoptedout")]
        private sealed class OptedOutExtensionConfigProvider : TestWebHookExtensionBase
        {
            public OptedOutExtensionConfigProvider()
                : base(OptedOutExtensionName)
            {
            }
        }

        // Name in the exclusion list
        [Extension("textextension", configurationSection: "textextension")]
        private sealed class ExcludedExtensionConfigProvider : TestWebHookExtensionBase
        {
            public ExcludedExtensionConfigProvider()
                : base(ExcludedExtensionName)
            {
            }
        }

        private abstract class TestWebHookExtensionBase : IExtensionConfigProvider, IAsyncConverter<HttpRequestMessage, HttpResponseMessage>
        {
            private readonly string _extensionName;

            protected TestWebHookExtensionBase(string extensionName)
            {
                _extensionName = extensionName;
            }

            public void Initialize(ExtensionConfigContext context)
            {
                // Trigger registration with DefaultScriptWebHookProvider and ensure a system key
                // is generated for the extension.
                _ = context.GetWebhookHandler();
            }

            public Task<HttpResponseMessage> ConvertAsync(HttpRequestMessage input, CancellationToken cancellationToken)
            {
                // Extensions detect ARM-bridged requests by inspecting the request bag using
                // the well-known ScriptConstants.AzureFunctionsArmWebhookRequestKey. The host
                // sets the value via HttpRequestMessage.Options, but extensions may also read
                // it through the legacy Properties bag (the two share underlying storage).
                bool isArmRequest = input.Options.TryGetValue(
                    new HttpRequestOptionsKey<bool>(ScriptConstants.AzureFunctionsArmWebhookRequestKey),
                    out bool armValue) && armValue;

                ExtensionInvocationInfo info = new()
                {
                    ExtensionName = _extensionName,
                    HttpMethod = input.Method.Method,
                    IsArmRequest = isArmRequest
                };

                HttpResponseMessage response = new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(info)
                };
                return Task.FromResult(response);
            }
        }
    }
}
