// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Net;
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
    public class SamplesEndToEndTests_Java : IClassFixture<SamplesEndToEndTests_Java.TestFixture>
    {
        private readonly ScriptSettingsManager _settingsManager;
        private TestFixture _fixture;

        public SamplesEndToEndTests_Java(TestFixture fixture)
        {
            _fixture = fixture;
            _settingsManager = ScriptSettingsManager.Instance;
        }

        [Fact]
        public async Task HttpTrigger_Java_Get_Succeeds()
        {
            var result = await SamplesTestHelpers.InvokeHttpTrigger(_fixture, "HttpTrigger");
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        }

        [Fact]
        public async Task JavaProcess_Different_AfterHostRestart()
        {
            IJobHostRpcWorkerChannelManager manager = _fixture.Host.JobHostServices.GetService<IJobHostRpcWorkerChannelManager>();
            var channels = manager.GetChannels("java").ToList();
            Assert.Equal(1, channels.Count);
            int processId = channels[0].WorkerProcess.Id;

            // Trigger a restart
            await _fixture.Host.RestartAsync(CancellationToken.None);
            await HttpTrigger_Java_Get_Succeeds();

            // Verify after restart we have only 1 java channel still, and the process ID has changed.
            manager = _fixture.Host.JobHostServices.GetService<IJobHostRpcWorkerChannelManager>();
            channels = manager.GetChannels("java").ToList();
            Assert.Equal(1, channels.Count);
            Assert.NotEqual(processId, channels[0].WorkerProcess.Id);
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture()
                : base(Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "..", "sample", "java"), "samples", RpcWorkerConstants.JavaLanguageWorkerName)
            {
            }

            public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
            {
                base.ConfigureScriptHost(webJobsBuilder);
            }
        }
    }
}