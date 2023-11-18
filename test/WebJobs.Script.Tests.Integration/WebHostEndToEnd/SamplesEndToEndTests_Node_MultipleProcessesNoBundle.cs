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

        [Fact(Skip = "https://github.com/Azure/azure-functions-host/issues")]
        public async Task NodeProcessNoBundleConfigured_Different_AfterHostRestart()
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