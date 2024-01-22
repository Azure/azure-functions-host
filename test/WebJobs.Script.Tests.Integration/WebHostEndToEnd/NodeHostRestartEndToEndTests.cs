// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd;

public class NodeHostRestartEndToEndTests
{
    [Fact]
    // Confirms that a background timer to create new worker processes does not
    // continue to fire after we've initiated a restart. This could lead to issues
    // where we'd create too many processes and throw an exception.
    // See https://github.com/Azure/azure-functions-host/pull/9820 for details.
    public static async Task JobHostRestart_StopsCreatingNewWorkers()
    {
        CancellationTokenRegistration registration = default;
        var fixture = new WorkerProcessRestartTestFixture();

        try
        {
            await fixture.InitializeAsync();
            var channelManager = fixture.Host.WebHostServices.GetService<IWebHostRpcWorkerChannelManager>();
            var scriptHostManager = fixture.Host.WebHostServices.GetService<IScriptHostManager>();
            var appHostLifecycle = fixture.Host.JobHostServices.GetService<IApplicationLifetime>();
            var semaphore = new SemaphoreSlim(0, 1);
            registration = appHostLifecycle.ApplicationStopping.Register(() =>
            {
                // pause here to prevent the original host from shutting down fully
                // this emulates scenarios in production where the disposal of an old host
                // can take a very long time
                semaphore.Wait();
            });

            await TestHelpers.Await(() =>
            {
                var channels = channelManager.GetChannels("node");
                int? currentChannelCount = channels?.Count;
                return currentChannelCount == 2;
            });

            // Once we've hit 2, we have a couple seconds to trigger a restart.
            _ = Task.Run(() => scriptHostManager.RestartHostAsync());

            DateTime start = DateTime.UtcNow;
            await TestHelpers.Await(() =>
            {
                var channels = channelManager.GetChannels("node");
                if (channels == null)
                {
                    return false;
                }

                // If it hasn't started by now, we should be good.
                var waitTime = WorkerProcessRestartTestFixture.ProcessStartupInterval.Add(TimeSpan.FromSeconds(2));
                if (DateTime.UtcNow.AddSeconds(-waitTime.TotalSeconds) > start)
                {
                    return true;
                }

                return channels.Count(c => c.Value.Task.Status == TaskStatus.RanToCompletion) == 4;
            });

            // let the original shutdown continue
            semaphore.Release();

            string key = await fixture.Host.GetFunctionSecretAsync("HttpTrigger");
            var result = await fixture.Host.HttpClient.GetAsync($"/api/HttpTrigger?code={key}");

            var errors = fixture.Host.GetScriptHostLogMessages()
                .Where(m => m.Level == LogLevel.Error)
                .Select(m => m.Exception);
            Assert.Empty(errors);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        }
        finally
        {
            await fixture.DisposeAsync();
            (registration as IDisposable)?.Dispose();
        }
    }
    private class WorkerProcessRestartTestFixture : EndToEndTestFixture
    {
        private static readonly string rootPath = Path.Combine("TestScripts", "Node");

        public WorkerProcessRestartTestFixture()
            : base(rootPath, "nodeWorkerRestart", RpcWorkerConstants.NodeLanguageWorkerName, workerProcessesCount: 3)
        {
        }

        public static readonly TimeSpan ProcessStartupInterval = TimeSpan.FromSeconds(3);

        protected override Task CreateTestStorageEntities() => Task.CompletedTask;

        public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
        {
            base.ConfigureScriptHost(webJobsBuilder);

            webJobsBuilder.AddAzureStorage()
                .Services.Configure<ScriptJobHostOptions>(o =>
                {
                    o.Functions = new[]
                    {
                        "HttpTrigger"
                    };
                });

            webJobsBuilder.Services.AddOptions<LanguageWorkerOptions>()
                .PostConfigure(o =>
                {
                    var nodeConfig = o.WorkerConfigs.Single(c => c.Description.Language == "node");
                    nodeConfig.CountOptions.ProcessStartupInterval = ProcessStartupInterval;
                });
        }
    }
}
