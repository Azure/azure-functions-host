// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.EndToEnd
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.SamplesEndToEnd)]
    public class SamplesEndToEndTests_Node_MultipleProcesses : IClassFixture<SamplesEndToEndTests_Node_MultipleProcesses.MultiplepleProcessesTestFixture>
    {
        private readonly ScriptSettingsManager _settingsManager;
        private MultiplepleProcessesTestFixture _fixture;
        private IEnumerable<int> _nodeProcessesBeforeTestStarted;

        public SamplesEndToEndTests_Node_MultipleProcesses(MultiplepleProcessesTestFixture fixture)
        {
            _fixture = fixture;
            _nodeProcessesBeforeTestStarted = fixture.NodeProcessesBeforeTestStarted;
            _settingsManager = ScriptSettingsManager.Instance;
        }

        [Fact]
        public async Task NodeProcess_Different_AfterHostRestart()
        {
            await SamplesTestHelpers.InvokeAndValidateHttpTrigger(_fixture, "HttpTrigger");
            IEnumerable<int> nodeProcessesBeforeHostRestart = Process.GetProcessesByName("node").Select(p => p.Id);
            // Trigger a restart
            await _fixture.Host.RestartAsync(CancellationToken.None);

            await SamplesTestHelpers.InvokeAndValidateHttpTrigger(_fixture, "HttpTrigger");

            // Wait for all the 3 process to start
            await Task.Delay(TimeSpan.FromMinutes(1));

            IEnumerable<int> nodeProcessesAfter = Process.GetProcessesByName("node").Select(p => p.Id);
           
            // Verify node process is different after host restart
            var result = nodeProcessesAfter.Where(pId1 => !nodeProcessesBeforeHostRestart.Any(pId2 => pId2 == pId1) && !_fixture.NodeProcessesBeforeTestStarted.Any(pId3 => pId3 == pId1));
            Assert.Equal(3, result.Count());
        }
        
        private async Task<IEnumerable<int>> WaitAndGetAllNodeProcesses(int expectedProcessCount)
        {
            IEnumerable<int> nodeProcessesBeforeHostRestart = Process.GetProcessesByName("node").Select(p => p.Id);
            while(nodeProcessesBeforeHostRestart.Count() < expectedProcessCount)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                nodeProcessesBeforeHostRestart = Process.GetProcessesByName("node").Select(p => p.Id);
            }

            return nodeProcessesBeforeHostRestart;
        }

        [Fact]
        public async Task NodeProcessCount_RemainsSame_AfterMultipleTimeouts()
        {
            // Wait for all the 3 process to start
            List<Task<HttpResponseMessage>> timeoutTasks = new List<Task<HttpResponseMessage>>();
            Task timeoutTask = Task.Delay(TimeSpan.FromMinutes(1));
            Task<IEnumerable<int>> getNodeTask = WaitAndGetAllNodeProcesses(3);
            Task result = await Task.WhenAny(getNodeTask, timeoutTask);
            if (result.Equals(timeoutTask))
            {
                throw new Exception("Failed to start all 3 node processes");
            }
            var oldHostInstanceId = _fixture.HostInstanceId;
            IEnumerable<int> nodeProcessesBeforeHostRestart = await getNodeTask;
            string functionKey = await _fixture.Host.GetFunctionSecretAsync("httptrigger-timeout");
            string uri = $"api/httptrigger-timeout?code={functionKey}&name=Yogi";

            // Send multiple requests that would timeout
            for (int i=0; i<5; ++i)
            {
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
                timeoutTasks.Add(_fixture.Host.HttpClient.SendAsync(request));
            }
            var results = await Task.WhenAll(timeoutTasks);
            foreach(var timeoutResult in results)
            {
                Assert.Equal(HttpStatusCode.InternalServerError, timeoutResult.StatusCode);  // Confirm response code after timeout (10 seconds)
            }

            IEnumerable<int> nodeProcessesAfter = Process.GetProcessesByName("node").Select(p => p.Id);

            // Confirm count remains the same
            Assert.Equal(nodeProcessesBeforeHostRestart.Count(), nodeProcessesAfter.Count());

           
            // Confirm all processes are different
            Assert.Equal(3, nodeProcessesAfter.Except(nodeProcessesBeforeHostRestart).Count());

            // Confirm host instance ids are the same
            Assert.Equal(oldHostInstanceId, _fixture.HostInstanceId);
        }

        public class MultiplepleProcessesTestFixture : EndToEndTestFixture
        {
            private IEnumerable<int> _nodeProcessesBeforeTestStarted;

            public IEnumerable<int> NodeProcessesBeforeTestStarted => _nodeProcessesBeforeTestStarted;

            static MultiplepleProcessesTestFixture()
            {
            }

            public MultiplepleProcessesTestFixture()
                : base(Path.Combine(Environment.CurrentDirectory, @"..", "..", "..", "..", "..", "sample", "node"), "samples", RpcWorkerConstants.NodeLanguageWorkerName, 3)
            {
                _nodeProcessesBeforeTestStarted = Process.GetProcessesByName("node").Select(p => p.Id);
                _nodeProcessesBeforeTestStarted = _nodeProcessesBeforeTestStarted ?? new List<int>();
            }

            public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
            {
                base.ConfigureScriptHost(webJobsBuilder);
                webJobsBuilder.Services.Configure<ScriptJobHostOptions>(o =>
                {
                    o.Functions = new[]
                    {
                        "HttpTrigger",
                        "HttpTrigger-Timeout",
                    };
                });
            }
        }
    }
}