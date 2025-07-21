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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.EndToEnd
{
    public abstract class SamplesEndToEndTests_Node_MultipleProcessesNoBundleBase<TFixture> : IClassFixture<TFixture>
        where TFixture : MultipleProcessesBaseTestFixtureNoBundles
    {
        protected readonly TFixture _fixture;

        public SamplesEndToEndTests_Node_MultipleProcessesNoBundleBase(TFixture fixture)
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
                });
        }
    }

    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.SamplesEndToEnd)]
    public class SamplesEndToEndTests_Node_MultipleProcessesNoBundle : SamplesEndToEndTests_Node_MultipleProcessesNoBundleBase<MultipleProcessesTestFixtureNoBundles>
    {
        public SamplesEndToEndTests_Node_MultipleProcessesNoBundle(MultipleProcessesTestFixtureNoBundles fixture) : base(fixture)
        {
        }
    }

    public abstract class MultipleProcessesBaseTestFixtureNoBundles : EndToEndTestFixture
    {
        protected IEnumerable<int> _nodeProcessesBeforeTestStarted;

        public virtual IEnumerable<int> NodeProcessesBeforeTestStarted => _nodeProcessesBeforeTestStarted;

        public MultipleProcessesBaseTestFixtureNoBundles(string rootPath, string testId, string functionsWorkerRuntime, int workerProcessesCount)
            : base(rootPath, testId, functionsWorkerRuntime, workerProcessesCount)
        {
            _nodeProcessesBeforeTestStarted = Process.GetProcessesByName("node").Select(p => p.Id);
            _nodeProcessesBeforeTestStarted = _nodeProcessesBeforeTestStarted ?? new List<int>();
        }
    }

    public class MultipleProcessesTestFixtureNoBundles : MultipleProcessesBaseTestFixtureNoBundles
    {
        private IEnumerable<int> _nodeProcessesBeforeTestStarted_Default;

        static MultipleProcessesTestFixtureNoBundles()
        {
        }

        public MultipleProcessesTestFixtureNoBundles()
            : base(Path.Combine(Environment.CurrentDirectory, @"..", "..", "..", "..", "sample", "NodeWithoutBundle"), "samples", RpcWorkerConstants.NodeLanguageWorkerName, 3)
        {
            _nodeProcessesBeforeTestStarted_Default = Process.GetProcessesByName("node").Select(p => p.Id);
            _nodeProcessesBeforeTestStarted_Default = _nodeProcessesBeforeTestStarted_Default ?? new List<int>();
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

    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.SamplesEndToEnd)]
    public class SamplesEndToEndTests_Node_MultipleProcessesNoBundle_DecoupledWorker
        : SamplesEndToEndTests_Node_MultipleProcessesNoBundleBase<MultipleProcessesTestFixtureNoBundlesDecoupledWorker>
    {
        public SamplesEndToEndTests_Node_MultipleProcessesNoBundle_DecoupledWorker(MultipleProcessesTestFixtureNoBundlesDecoupledWorker fixture)
            : base(fixture)
        {
        }
    }

    public class MultipleProcessesTestFixtureNoBundlesDecoupledWorker : MultipleProcessesTestFixtureNoBundles
    {
        private IEnumerable<int> _nodeProcessesBeforeTestStarted_DecoupledWorker;

        static MultipleProcessesTestFixtureNoBundlesDecoupledWorker()
        {
        }

        public MultipleProcessesTestFixtureNoBundlesDecoupledWorker()
        {
            _nodeProcessesBeforeTestStarted_DecoupledWorker = Process.GetProcessesByName("node").Select(p => p.Id);
            _nodeProcessesBeforeTestStarted_DecoupledWorker = _nodeProcessesBeforeTestStarted_DecoupledWorker ?? new List<int>();
        }

        public override void ConfigureWebHost(IServiceCollection services)
        {
            base.ConfigureWebHost(services);

            services.Configure<FunctionsHostingConfigOptions>(o => o.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, "node"));
        }

        public override void ConfigureWebHost(IConfigurationBuilder configBuilder)
        {
            var inMemorySettings = new Dictionary<string, string>();
            inMemorySettings["languageWorkers:probingPaths:0"] = Path.GetFullPath("decoupledWorkers");

            configBuilder.AddInMemoryCollection(inMemorySettings);
        }
    }
}