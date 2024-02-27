// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    public class SamplesEndToEndTests_Node_MultipleProcessesNoBundle : IClassFixture<SamplesEndToEndTests_Node_MultipleProcessesNoBundle.MultiplepleProcessesTestFixtureNoBundles>
    {
        private readonly ScriptSettingsManager _settingsManager;
        private MultiplepleProcessesTestFixtureNoBundles _fixture;
        private IEnumerable<int> _nodeProcessesBeforeTestStarted;

        public SamplesEndToEndTests_Node_MultipleProcessesNoBundle(MultiplepleProcessesTestFixtureNoBundles fixture)
        {
            _fixture = fixture;
            _nodeProcessesBeforeTestStarted = fixture.NodeProcessesBeforeTestStarted;
            _settingsManager = ScriptSettingsManager.Instance;
        }

        [Fact]
        public async Task NodeProcessNoBundleConfigured_Different_AfterHostRestart()
        {
            await SamplesTestHelpers.InvokeAndValidateHttpTrigger(_fixture, "HttpTrigger");
            IEnumerable<int> nodeProcessesBeforeHostRestart = Process.GetProcessesByName("node").Select(p => p.Id);
            // Trigger a restart
            await _fixture.Host.RestartAsync(CancellationToken.None);

            await SamplesTestHelpers.InvokeAndValidateHttpTrigger(_fixture, "HttpTrigger");

            Console.WriteLine("--- BEFORE: " + string.Join(",", nodeProcessesBeforeHostRestart));

            // Wait for all the 3 process to start
            await TestHelpers.Await(() =>
                {
                    IEnumerable<int> nodeProcessesAfter = Process.GetProcessesByName("node").Select(p => p.Id);
                    Console.WriteLine("--- AFTER: " + string.Join(", ", nodeProcessesAfter));
                    // Verify node process is different after host restart
                    var result = nodeProcessesAfter.Where(pId1 => !nodeProcessesBeforeHostRestart.Any(pId2 => pId2 == pId1) && !_fixture.NodeProcessesBeforeTestStarted.Any(pId3 => pId3 == pId1));
                    bool success = result.Count() == 3;
                    return success;
                }, timeout: 120000, pollingInterval: 1000);
        }

        public class MultiplepleProcessesTestFixtureNoBundles : EndToEndTestFixture
        {
            private IEnumerable<int> _nodeProcessesBeforeTestStarted;

            public IEnumerable<int> NodeProcessesBeforeTestStarted => _nodeProcessesBeforeTestStarted;

            static MultiplepleProcessesTestFixtureNoBundles()
            {
            }

            public MultiplepleProcessesTestFixtureNoBundles()
                : base(Path.Combine(Environment.CurrentDirectory, @"..", "..", "..", "..", "..", "sample", "NodeWithoutBundle"), "samples", RpcWorkerConstants.NodeLanguageWorkerName, 3)
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
                        "HttpTrigger"
                    };
                });
            }
        }
    }
}