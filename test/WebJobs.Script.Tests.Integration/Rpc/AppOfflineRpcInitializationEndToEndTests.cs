// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests;

public class AppOfflineRpcInitializationEndToEndTests
{
    // Regression test for https://github.com/Azure/azure-functions-host/issues/11741
    [Fact]
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, "NodeEndToEndTests")]
    public async Task AppOfflineRemoved_RestartedHostHasOperationalRpcChannel()
    {
        var fixture = new TestFixture();

        try
        {
            // Fixture will start with the app in an offline state. The host should not initialize RPC channels in this state.
            await fixture.InitializeAsync();

            var scriptHostManager = fixture.Host.WebHostServices.GetRequiredService<IScriptHostManager>();
            Assert.Equal(ScriptHostState.Offline, scriptHostManager.State);
            string offlineHostInstanceId = await fixture.GetActiveHostInstanceIdAsync();

            // Wait for the host to be fully started before performing validations or removing the app_offline.htm file.
            // This ensures file watchers are fully initialized and the host will be able to detect the removal of the file.
            await fixture.Host.InitialScriptHostStarted.WaitAsync(System.TimeSpan.FromSeconds(30));

            // Worker channel should not be initialized when the app is offline.
            var dispatcherFactory = fixture.Host.JobHostServices.GetRequiredService<IFunctionInvocationDispatcherFactory>();
            var dispatcher = Assert.IsType<RpcFunctionInvocationDispatcher>(dispatcherFactory.GetFunctionDispatcher());
            IRpcWorkerChannel[] workerChannels = (await dispatcher.GetInitializedWorkerChannelsAsync()).ToArray();
            Assert.Empty(workerChannels);

            // Remove the app_offline.htm file and wait for the host to transition to running state.
            File.Delete(Path.Combine(fixture.RootScriptPath, ScriptConstants.AppOfflineFileName));

            await TestHelpers.Await(
                () => scriptHostManager.State == ScriptHostState.Running,
                pollingInterval: 100,
                timeout: 60_000,
                userMessageCallback: () => $"Host did not transition online.{System.Environment.NewLine}{fixture.Host.GetLog()}");

            // Verify that the host instance ID has changed, indicating that the host has restarted.
            string onlineHostInstanceId = await fixture.GetActiveHostInstanceIdAsync();
            Assert.NotEqual(offlineHostInstanceId, onlineHostInstanceId);

            // Verify that worker channels have been initialized.
            await TestHelpers.Await(
                async () =>
                {
                    var jobHostServices = fixture.Host.JobHostServices;
                    if (jobHostServices == null)
                    {
                        return false;
                    }

                    dispatcherFactory = jobHostServices.GetRequiredService<IFunctionInvocationDispatcherFactory>();
                    if (dispatcherFactory.GetFunctionDispatcher() is not RpcFunctionInvocationDispatcher rpcDispatcher)
                    {
                        return false;
                    }

                    workerChannels = (await rpcDispatcher.GetInitializedWorkerChannelsAsync()).ToArray();

                    return workerChannels.Length > 0;
                },
                pollingInterval: 100,
                timeout: 60_000,
                userMessageCallback: () => $"RPC worker channel did not become ready.{System.Environment.NewLine}{fixture.Host.GetLog()}");

            Assert.Equal(1, workerChannels.Length);

            // Verify that the function can be invoked successfully.
            string key = await fixture.Host.GetFunctionSecretAsync("HttpTrigger");
            using var response = await fixture.Host.HttpClient.GetAsync($"/api/HttpTrigger?code={key}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    private sealed class TestFixture : EndToEndTestFixture
    {
        public TestFixture()
            : base(
                Path.Combine("TestScripts", "Node"),
                "appOfflineRpcInitialization",
                RpcWorkerConstants.NodeLanguageWorkerName,
                startOffline: true)
        {
        }

        protected override Task CreateTestStorageEntities() => Task.CompletedTask;

        public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
        {
            base.ConfigureScriptHost(webJobsBuilder);
            webJobsBuilder.Services.Configure<ScriptJobHostOptions>(options =>
            {
                options.Functions = ["HttpTrigger"];
            });
        }
    }
}
