// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    public class SamplesEndToEndTests_Node_MultipleProcessesNoBundle : IClassFixture<SamplesEndToEndTests_Node_MultipleProcessesNoBundle.MultiplepleProcessesTestFixtureNoBundles>
    {
        private readonly MultiplepleProcessesTestFixtureNoBundles _fixture;

        public SamplesEndToEndTests_Node_MultipleProcessesNoBundle(MultiplepleProcessesTestFixtureNoBundles fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task NodeProcessNoBundleConfigured_Different_AfterHostRestart()
        {
            await SamplesTestHelpers.InvokeAndValidateHttpTrigger(_fixture, "HttpTrigger");
            IEnumerable<int> nodeProcessesBeforeHostRestart = Process.GetProcessesByName("node").Select(p => p.Id).ToArray();

            // Trigger a restart
            await _fixture.Host.RestartAsync(CancellationToken.None);

            await SamplesTestHelpers.InvokeAndValidateHttpTrigger(_fixture, "HttpTrigger");

            // Wait for all the 3 process to start
            await TestHelpers.Await(() =>
                {
                    IEnumerable<int> nodeProcessesAfter = Process.GetProcessesByName("node").Select(p => p.Id);
                    // Verify node process is different after host restart
                    var result = nodeProcessesAfter.Where(pId1 => !nodeProcessesBeforeHostRestart.Any(pId2 => pId2 == pId1) && !_fixture.NodeProcessesBeforeTestStarted.Any(pId3 => pId3 == pId1));
                    return result.Count() == 3;
                },
                userMessageCallback: () => GetRestartDiagnostics(nodeProcessesBeforeHostRestart));
        }

        private string GetRestartDiagnostics(IEnumerable<int> nodeProcessesBeforeHostRestart)
        {
            int[] nodeProcessesAfter = Process.GetProcessesByName("node").Select(p => p.Id).ToArray();
            int[] newNodeProcesses = nodeProcessesAfter
                .Where(pId1 => !nodeProcessesBeforeHostRestart.Any(pId2 => pId2 == pId1) && !_fixture.NodeProcessesBeforeTestStarted.Any(pId3 => pId3 == pId1))
                .ToArray();

            var webHostChannelManager = _fixture.Host.WebHostServices.GetService<IWebHostRpcWorkerChannelManager>();
            var webHostChannels = webHostChannelManager?.GetChannels(RpcWorkerConstants.NodeLanguageWorkerName);
            var jobHostChannelManager = _fixture.Host.JobHostServices?.GetService<IJobHostRpcWorkerChannelManager>();
            var jobHostChannels = jobHostChannelManager?.GetChannels(RpcWorkerConstants.NodeLanguageWorkerName);

            return $"""
                Expected 3 new node processes after host restart.
                Node processes before test started: {FormatProcessIds(_fixture.NodeProcessesBeforeTestStarted)}
                Node processes before host restart: {FormatProcessIds(nodeProcessesBeforeHostRestart)}
                Node processes after host restart: {FormatProcessIds(nodeProcessesAfter)}
                New node processes after host restart: {FormatProcessIds(newNodeProcesses)}
                WebHost node channel count: {webHostChannels?.Count}
                JobHost node channel count: {jobHostChannels?.Count()}
                Host logs:
                {_fixture.Host.GetLog()}
                """;
        }

        private static string FormatProcessIds(IEnumerable<int> processIds)
        {
            return string.Join(", ", processIds.OrderBy(p => p));
        }

        public class MultiplepleProcessesTestFixtureNoBundles : EndToEndTestFixture
        {
            private IEnumerable<int> _nodeProcessesBeforeTestStarted;

            public IEnumerable<int> NodeProcessesBeforeTestStarted => _nodeProcessesBeforeTestStarted;

            static MultiplepleProcessesTestFixtureNoBundles()
            {
            }

            public MultiplepleProcessesTestFixtureNoBundles()
                : base(Path.Combine(Environment.CurrentDirectory, @"..", "..", "..", "..", "sample", "NodeWithoutBundle"), "samples", RpcWorkerConstants.NodeLanguageWorkerName, 3)
            {
                _nodeProcessesBeforeTestStarted = Process.GetProcessesByName("node").Select(p => p.Id).ToArray();
                _nodeProcessesBeforeTestStarted = _nodeProcessesBeforeTestStarted ?? new List<int>();
            }

            public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
            {
                base.ConfigureScriptHost(webJobsBuilder);
                webJobsBuilder.Services.Configure<ScriptJobHostOptions>(o =>
                {
                    o.Functions = new[]
                    {
                        "HttpTrigger"
                    };
                });
            }
        }
    }
}