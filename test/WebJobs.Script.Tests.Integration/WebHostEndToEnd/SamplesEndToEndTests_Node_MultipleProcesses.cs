// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.EndToEnd
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.SamplesEndToEnd)]
    public class SamplesEndToEndTests_Node_MultipleProcesses : IAsyncDisposable
    {
        private readonly MultiplepleProcessesTestFixture _fixture;

        public SamplesEndToEndTests_Node_MultipleProcesses()
        {
            // We want a new fixture for each test
            _fixture = new MultiplepleProcessesTestFixture();
        }

        [Fact]
        public async Task NodeProcess_Different_AfterHostRestart()
        {
            await _fixture.InitializeAsync();
            await WaitForWebHostChannelCountAsync(3);

            await SamplesTestHelpers.InvokeAndValidateHttpTrigger(_fixture, "HttpTrigger");
            IEnumerable<int> nodeProcessesBeforeHostRestart = GetCurrentWorkerPids();

            // Trigger a restart
            await _fixture.Host.RestartAsync(CancellationToken.None);

            await SamplesTestHelpers.InvokeAndValidateHttpTrigger(_fixture, "HttpTrigger");

            // Wait for all the 3 process to start
            await WaitForWebHostChannelCountAsync(3);

            IEnumerable<int> nodeProcessesAfter = GetCurrentWorkerPids();

            // Verify node process is different after host restart
            var result = nodeProcessesAfter.Where(pId1 => !nodeProcessesBeforeHostRestart.Any(pId2 => pId2 == pId1) && !nodeProcessesBeforeHostRestart.Any(pId3 => pId3 == pId1));
            Assert.Equal(3, result.Count());
        }

        [Fact]
        public async Task NodeProcessCount_RemainsSame_AfterMultipleTimeouts()
        {
            await _fixture.InitializeAsync();

            // Wait for all the 3 process to start
            List<Task<HttpResponseMessage>> timeoutTasks = new List<Task<HttpResponseMessage>>();
            await WaitForWebHostChannelCountAsync(3);
            IEnumerable<int> nodeProcessesBeforeHostRestart = GetCurrentWorkerPids();

            var oldHostInstanceId = await _fixture.GetActiveHostInstanceIdAsync();

            string functionKey = await _fixture.Host.GetFunctionSecretAsync("httptrigger-timeout");
            string uri = $"api/httptrigger-timeout?code={functionKey}&name=Yogi";

            // Send multiple requests that would timeout
            for (int i = 0; i < 5; ++i)
            {
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
                timeoutTasks.Add(_fixture.Host.HttpClient.SendAsync(request));
            }

            var results = await Task.WhenAll(timeoutTasks);
            foreach (var timeoutResult in results)
            {
                Assert.Equal(HttpStatusCode.InternalServerError, timeoutResult.StatusCode);  // Confirm response code after timeout (10 seconds)
            }

            // restarted workers run at JobHost level (?)
            await WaitForJobHostChannelCountAsync(3);
            var nodeProcessesAfter = GetCurrentWorkerPids();

            // Confirm count remains the same
            Assert.Equal(nodeProcessesBeforeHostRestart.Count(), nodeProcessesAfter.Count());

            // Confirm host instance ids are the same
            Assert.Equal(oldHostInstanceId, await _fixture.GetActiveHostInstanceIdAsync());

            var result = nodeProcessesAfter.Where(pId1 => !nodeProcessesBeforeHostRestart.Any(pId2 => pId2 == pId1) && !nodeProcessesBeforeHostRestart.Any(pId3 => pId3 == pId1));
            Assert.Equal(3, result.Count());
        }

        public async Task WaitForWebHostChannelCountAsync(int expectedCount)
        {
            await TestHelpers.Await(() =>
            {
                var channelManager = _fixture.Host.WebHostServices.GetService<IWebHostRpcWorkerChannelManager>();
                var channels = channelManager.GetChannels("node");
                return channels is not null && channels.Count == expectedCount;
            }, pollingInterval: 1000, userMessageCallback: _fixture.Host.GetLog);
        }

        public async Task WaitForJobHostChannelCountAsync(int expectedCount)
        {
            await TestHelpers.Await(() =>
            {
                var channelManager = _fixture.Host.JobHostServices.GetService<IJobHostRpcWorkerChannelManager>();
                var channels = channelManager.GetChannels("node");
                return channels is not null && channels.Count() == expectedCount;
            }, pollingInterval: 1000, userMessageCallback: _fixture.Host.GetLog);
        }

        public IEnumerable<int> GetCurrentWorkerPids()
        {
            var webHostChannelManager = _fixture.Host.WebHostServices.GetService<IWebHostRpcWorkerChannelManager>();
            var webHostChannels = webHostChannelManager.GetChannels("node");
            var webHostPids = webHostChannels.Select(c => c.Value.Task.Result.WorkerProcess.Id).ToArray();

            var jobHostChannelManager = _fixture.Host.JobHostServices.GetService<IJobHostRpcWorkerChannelManager>();
            var jobHostChannels = jobHostChannelManager.GetChannels("node");
            var jobHostPids = jobHostChannels.Select(c => c.WorkerProcess.Id).ToArray();

            return webHostPids.Concat(jobHostPids);
        }

        public async ValueTask DisposeAsync()
        {
            await _fixture.DisposeAsync();
        }

        public class MultiplepleProcessesTestFixture : EndToEndTestFixture
        {
            public MultiplepleProcessesTestFixture()
                : base(Path.Combine(Environment.CurrentDirectory, @"..", "..", "..", "..", "sample", "node"), "samples", RpcWorkerConstants.NodeLanguageWorkerName, 3)
            {
            }

            public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
            {
                base.ConfigureScriptHost(webJobsBuilder);
                webJobsBuilder.Services.Configure<ScriptJobHostOptions>(o =>
                {
                    o.Functions =
                    [
                        "HttpTrigger",
                        "HttpTrigger-Timeout",
                    ];
                });

                webJobsBuilder.Services.AddOptions<LanguageWorkerOptions>()
                    .PostConfigure(o =>
                    {
                        var nodeConfig = o.WorkerConfigs.Single(c => c.Description.Language == "node");
                        nodeConfig.CountOptions.ProcessStartupInterval = TimeSpan.FromSeconds(3);
                    });
            }
        }
    }
}