// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Tests;
using Microsoft.Azure.WebJobs.Script.Tests.EndToEnd;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.EndToEnd
{
    public class DecoupleWorkers_NodeEndToEndTests : NodeEndToEndTestsBase<DecoupleWorkers_NodeEndToEndTests.TestFixture>
    {
        private TestFixture Fixture1 { get; }

        public DecoupleWorkers_NodeEndToEndTests(TestFixture fixture) : base(fixture)
        {
            this.Fixture1 = fixture;
        }

        public class TestFixture() : EndToEndTestFixture(rootPath, "node", RpcWorkerConstants.NodeLanguageWorkerName)
        {
            private static readonly string rootPath = Path.Combine("TestScripts", "Node");

            public override void ConfigureWebHost(IServiceCollection services)
            {
                base.ConfigureWebHost(services);

                Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagEnableWorkerProbingPaths);
            }

            public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
            {
                base.ConfigureScriptHost(webJobsBuilder);

                webJobsBuilder.AddAzureStorage()
                    .Services.Configure<ScriptJobHostOptions>(o =>
                    {
                        o.Functions =
                        [
                            "BlobTriggerToBlob",
                            "HttpTrigger",
                            "HttpTrigger-Scenarios",
                            "HttpTriggerExpressApi",
                            "HttpTriggerPromise",
                            "HttpTriggerToBlob",
                            "Invalid",
                            "ManualTrigger",
                            "MultipleExports",
                            "MultipleOutputs",
                            "MultipleInputs",
                            "QueueTriggerByteArray",
                            "QueueTriggerToBlob",
                            "SingleNamedExport",
                            "TableIn",
                            "TableOut",
                            "TimerTrigger",
                            "Scenarios"
                        ];
                    });
            }
        }

        [Fact]
        public async Task CheckLogs()
        {
            var logs = await TestHelpers.GetHostLogsAsync();
            List<string> result = logs.Where(s => s != null && s.Contains("Found required workerConfig c:\\testData\\workers\\node\\3.10.1\\")).ToList();
            Assert.True(result.Any());
        }
    }
}
