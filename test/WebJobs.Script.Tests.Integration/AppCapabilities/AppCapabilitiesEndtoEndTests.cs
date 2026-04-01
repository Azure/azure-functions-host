// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.AppCapabilities;

public class AppCapabilitiesEndToEndTests
{
    [Fact]
    public async Task HostRestart_ClearsOldCapabilities_AndAcceptsNewWorkerCapabilities()
    {
        var fixture = new AppCapabilitiesTestFixture();

        try
        {
            await fixture.InitializeAsync();

            // Get the capabilities store from the web host services (singleton across host restarts)
            var capabilitiesStore = fixture.Host.WebHostServices.GetService<IAppCapabilitiesStore>();
            Assert.NotNull(capabilitiesStore);

            // Simulate first worker registering capabilities
            var firstCapabilities = new Dictionary<string, string>
            {
                ["SupportsFeatureA"] = "true",
                ["FrameworkVersion"] = "1.0.0",
                ["MaxConcurrency"] = "10"
            };

            bool firstSetResult = capabilitiesStore.TrySetAll(firstCapabilities);
            Assert.True(firstSetResult);
            Assert.Equal(3, capabilitiesStore.Capabilities.Count);
            Assert.Equal("true", capabilitiesStore.Capabilities["SupportsFeatureA"]);
            Assert.Equal("1.0.0", capabilitiesStore.Capabilities["FrameworkVersion"]);
            Assert.Equal("10", capabilitiesStore.Capabilities["MaxConcurrency"]);

            // Get the options before restart
            var optionsBeforeRestart = fixture.Host.JobHostServices.GetService<Microsoft.Extensions.Options.IOptionsMonitor<AppCapabilitiesOptions>>();
            Assert.NotNull(optionsBeforeRestart);
            var optionsValueBefore = optionsBeforeRestart.CurrentValue;
            Assert.Equal(3, ((System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<string, string>>)optionsValueBefore).Count);

            // Trigger a host restart
            var scriptHostManager = fixture.Host.WebHostServices.GetService<IScriptHostManager>();
            Assert.NotNull(scriptHostManager);
            await scriptHostManager.RestartHostAsync("test");

            // Wait for the host to be running again
            await TestHelpers.Await(() =>
            {
                return scriptHostManager.State == ScriptHostState.Running;
            }, timeout: 30000);

            // Verify capabilities were cleared during restart
            Assert.Throws<InvalidOperationException>(() => { var _ = capabilitiesStore.Capabilities; });

            // Verify options reflect the cleared state
            var optionsAfterRestart = fixture.Host.WebHostServices.GetService<Microsoft.Extensions.Options.IOptions<AppCapabilitiesOptions>>();
            Assert.NotNull(optionsAfterRestart);
            var optionsValueAfter = optionsAfterRestart.Value;
            Assert.Empty((System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<string, string>>)optionsValueAfter);

            // Simulate a new worker starting with different capabilities
            var secondCapabilities = new Dictionary<string, string>
            {
                ["SupportsFeatureB"] = "true",
                ["FrameworkVersion"] = "2.0.0",
                ["RuntimeLanguage"] = "node"
            };

            bool secondSetResult = capabilitiesStore.TrySetAll(secondCapabilities);
            Assert.True(secondSetResult);
            Assert.Equal(3, capabilitiesStore.Capabilities.Count);

            // Verify only new capabilities are present
            Assert.False(capabilitiesStore.Capabilities.ContainsKey("SupportsFeatureA"));
            Assert.False(capabilitiesStore.Capabilities.ContainsKey("MaxConcurrency"));
            Assert.True(capabilitiesStore.Capabilities.ContainsKey("SupportsFeatureB"));
            Assert.Equal("true", capabilitiesStore.Capabilities["SupportsFeatureB"]);
            Assert.Equal("2.0.0", capabilitiesStore.Capabilities["FrameworkVersion"]);
            Assert.Equal("node", capabilitiesStore.Capabilities["RuntimeLanguage"]);

            // Verify the HTTP endpoint still works
            string key = await fixture.Host.GetFunctionSecretAsync("HttpTrigger");
            var result = await fixture.Host.HttpClient.GetAsync($"/api/HttpTrigger?code={key}");
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    private class AppCapabilitiesTestFixture : EndToEndTestFixture
    {
        private static readonly string _rootPath = Path.Combine("TestScripts", "Node");

        public AppCapabilitiesTestFixture()
            : base(_rootPath, "appCapabilitiesTest", RpcWorkerConstants.NodeLanguageWorkerName)
        {
        }

        protected override Task CreateTestStorageEntities() => Task.CompletedTask;

        public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
        {
            base.ConfigureScriptHost(webJobsBuilder);

            webJobsBuilder.Services.Configure<ScriptJobHostOptions>(o =>
            {
                o.Functions = new[] { "HttpTrigger" };
            });
        }
    }
}
