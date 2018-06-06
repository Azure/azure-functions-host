// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{
    public class WorkerConfigFactoryTests
    {
        [Fact]
        public void SetsDebugPort()
        {
            var lang = "test";
            var extension = ".test";
            var defaultWorkerPath = "./test";

            var workerPath = "/path/to/custom/worker";

            var config = new ConfigurationBuilder()
                 .AddInMemoryCollection(new Dictionary<string, string>
                 {
                     [$"{LanguageWorkerConstants.LanguageWorkerSectionName}:{lang}:arguments"] = "--inspect=1000",
                     [$"{LanguageWorkerConstants.LanguageWorkerSectionName}:{lang}:path"] = workerPath
                 })
                .Build();

            var logger = new TestLogger("test");
            var workerConfigFactory = new WorkerConfigFactory(config, logger);

            var workerConfigs = workerConfigFactory.GetConfigs(new List<IWorkerProvider>()
            {
                new TestWorkerProvider()
                {
                    Language = lang,
                    Extension = extension,
                    DefaultWorkerPath = defaultWorkerPath
                }
            });

            Assert.Equal(workerConfigs.Single().Arguments.WorkerPath, workerPath);
        }

        [Fact]
        public void DefaultLanguageWorkersDir()
        {
            var expectedWorkersDir = Path.Combine(Path.GetDirectoryName(new Uri(typeof(WorkerConfigFactory).Assembly.CodeBase).LocalPath), LanguageWorkerConstants.DefaultWorkersDirectoryName);
            var config = new ConfigurationBuilder().Build();
            var testLogger = new TestLogger("test");
            var configFactory = new WorkerConfigFactory(config, testLogger);
            Assert.Equal(expectedWorkersDir, configFactory.WorkerDirPath);
        }

        [Fact]
        public void LanguageWorker_WorkersDir_Set()
        {
            var expectedWorkersDir = @"d:\testWorkersDir";
            var config = new ConfigurationBuilder()
                   .AddInMemoryCollection(new Dictionary<string, string>
                   {
                       ["languageWorker:workersDirectory"] = expectedWorkersDir
                   })
                   .Build();
            var testLogger = new TestLogger("test");
            var configFactory = new WorkerConfigFactory(config, testLogger);
            Assert.Equal(expectedWorkersDir, configFactory.WorkerDirPath);
        }

        [Fact]
        public void LanguageWorker_WorkersDir_NotSet()
        {
            var expectedWorkersDir = Path.Combine(Path.GetDirectoryName(new Uri(typeof(WorkerConfigFactory).Assembly.CodeBase).LocalPath), LanguageWorkerConstants.DefaultWorkersDirectoryName);
            var configBuilder = ScriptSettingsManager.CreateDefaultConfigurationBuilder()
                   .AddInMemoryCollection(new Dictionary<string, string>
                   {
                       ["languageWorker"] = "test"
                   });
            var config = configBuilder.Build();
            var scriptSettingsManager = new ScriptSettingsManager(config);
            var testLogger = new TestLogger("test");
            var configFactory = new WorkerConfigFactory(config, testLogger);
            Assert.Equal(expectedWorkersDir, configFactory.WorkerDirPath);
        }
    }
}