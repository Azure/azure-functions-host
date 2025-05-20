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
using OpenTelemetry.Trace;
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
        private readonly string ProbingPath = Path.GetFullPath("..\\..\\..\\..\\test\\TestWorkers\\ProbingPaths\\workers\\");

        public DecoupleWorkers_NodeEndToEndTests(TestFixture fixture) : base(fixture)
        {
        }

        public class TestFixture() : EndToEndTestFixture(rootPath, "node", RpcWorkerConstants.NodeLanguageWorkerName)
        {
            private static readonly string rootPath = Path.Combine("TestScripts", "Node");
            private readonly string ProbingPath = Path.GetFullPath("..\\..\\..\\..\\test\\TestWorkers\\ProbingPaths\\workers\\");

            public override void ConfigureWebHost(IServiceCollection services)
            {
                base.ConfigureWebHost(services);

              //  CopyDirectory(Path.GetFullPath("workers")+"\\node", ProbingPath+"node\\3.10.1");

                Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagEnableWorkerProbingPaths);
                Environment.SetEnvironmentVariable(EnvironmentSettingNames.WorkerProbingPaths, $"{ProbingPath};");
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

            public static void CopyDirectory(string sourceDir, string destDir)
            {
                // Create destination directory if it doesn't exist
                Directory.CreateDirectory(destDir);

                // Copy all files
                foreach (var file in Directory.GetFiles(sourceDir))
                {
                    var destFile = Path.Combine(destDir, Path.GetFileName(file));
                    File.Copy(file, destFile, overwrite: true);
                }

                // Recursively copy subdirectories
                foreach (var dir in Directory.GetDirectories(sourceDir))
                {
                    var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
                    CopyDirectory(dir, destSubDir);
                }
            }

            public static void DeleteDirectoryContents(string dir)
            {
                if (!Directory.Exists(dir))
                    return;

                // Delete all files
                foreach (var file in Directory.GetFiles(dir))
                {
                    File.Delete(file);
                }

                // Delete all subdirectories and their contents
                foreach (var subDir in Directory.GetDirectories(dir))
                {
                    Directory.Delete(subDir, recursive: true);
                }
            }
        }

        [Fact]
        public async Task CheckLogs()
        {
            var logs = await TestHelpers.GetHostLogsAsync();
            List<string> result = logs.Where(s => s != null && s.Contains($"Found required workerConfig {ProbingPath}node\\3.10.1")).ToList();
            Assert.True(result.Any());
        }
    }
}
