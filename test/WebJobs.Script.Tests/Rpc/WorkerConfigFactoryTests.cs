// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{
    public class WorkerConfigFactoryTests
    {
        [Fact]
        public void DefaultLanguageWorkersDir()
        {
            var expectedWorkersDir = Path.Combine(Path.GetDirectoryName(new Uri(typeof(WorkerConfigFactory).Assembly.CodeBase).LocalPath), LanguageWorkerConstants.DefaultWorkersDirectoryName);
            var config = new ConfigurationBuilder().Build();
            var testLogger = new TestLogger("test");
            var configFactory = new WorkerConfigFactory(config, testLogger);
            Assert.Equal(expectedWorkersDir, configFactory.WorkersDirPath);
        }

        [Fact]
        public void LanguageWorker_WorkersDir_Set()
        {
            var expectedWorkersDir = @"d:\testWorkersDir";
            var config = new ConfigurationBuilder()
                   .AddInMemoryCollection(new Dictionary<string, string>
                   {
                       [$"{LanguageWorkerConstants.LanguageWorkersSectionName}:{LanguageWorkerConstants.WorkersDirectorySectionName}"] = expectedWorkersDir
                   })
                   .Build();
            var testLogger = new TestLogger("test");
            var configFactory = new WorkerConfigFactory(config, testLogger);
            Assert.Equal(expectedWorkersDir, configFactory.WorkersDirPath);
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
            Assert.Equal(expectedWorkersDir, configFactory.WorkersDirPath);
        }

        [Fact]
        public void JavaPath_AppServiceEnv()
        {
            var configBuilder = ScriptSettingsManager.CreateDefaultConfigurationBuilder()
                  .AddInMemoryCollection(new Dictionary<string, string>
                  {
                      ["languageWorker"] = "test"
                  });
            var config = configBuilder.Build();
            var scriptSettingsManager = new ScriptSettingsManager(config);
            var testLogger = new TestLogger("test");
            var configFactory = new WorkerConfigFactory(config, testLogger);
            var testEnvVariables = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsiteInstanceId, "123" },
                { "JAVA_HOME", @"D:\Program Files\Java\jdk1.7.0_51" }
            };
            using (var variables = new TestScopedSettings(scriptSettingsManager, testEnvVariables))
            {
                var javaPath = configFactory.GetExecutablePathForJava("../../zulu8.23.0.3-jdk8.0.144-win_x64/bin/java");
                Assert.Equal(@"D:\Program Files\Java\zulu8.23.0.3-jdk8.0.144-win_x64\bin\java", javaPath);
            }
        }

        [Fact]
        public void JavaPath_JavaHome_Set()
        {
            var configBuilder = ScriptSettingsManager.CreateDefaultConfigurationBuilder()
                  .AddInMemoryCollection(new Dictionary<string, string>
                  {
                      ["languageWorker"] = "test"
                  });
            var config = configBuilder.Build();
            var scriptSettingsManager = new ScriptSettingsManager(config);
            var testLogger = new TestLogger("test");
            var configFactory = new WorkerConfigFactory(config, testLogger);
            var testEnvVariables = new Dictionary<string, string>
            {
                { "JAVA_HOME", @"D:\Program Files\Java\jdk1.7.0_51" }
            };
            using (var variables = new TestScopedSettings(scriptSettingsManager, testEnvVariables))
            {
                var javaPath = configFactory.GetExecutablePathForJava("java");
                Assert.Equal(@"D:\Program Files\Java\jdk1.7.0_51\bin\java", javaPath);
            }
        }

        [Fact]
        public void JavaPath_JavaHome_Set_DefaultExePathSet()
        {
            var configBuilder = ScriptSettingsManager.CreateDefaultConfigurationBuilder()
                  .AddInMemoryCollection(new Dictionary<string, string>
                  {
                      ["languageWorker"] = "test"
                  });
            var config = configBuilder.Build();
            var scriptSettingsManager = new ScriptSettingsManager(config);
            var testLogger = new TestLogger("test");
            var configFactory = new WorkerConfigFactory(config, testLogger);
            var testEnvVariables = new Dictionary<string, string>
            {
                { "JAVA_HOME", @"D:\Program Files\Java\jdk1.7.0_51" }
            };
            using (var variables = new TestScopedSettings(scriptSettingsManager, testEnvVariables))
            {
                var javaPath = configFactory.GetExecutablePathForJava(@"D:\MyCustomPath\Java");
                Assert.Equal(@"D:\MyCustomPath\Java", javaPath);
            }
        }

        [Fact]
        public void JavaPath_JavaHome_NotSet()
        {
            var configBuilder = ScriptSettingsManager.CreateDefaultConfigurationBuilder()
                  .AddInMemoryCollection(new Dictionary<string, string>
                  {
                      ["languageWorker"] = "test"
                  });
            var config = configBuilder.Build();
            var scriptSettingsManager = new ScriptSettingsManager(config);
            var testLogger = new TestLogger("test");
            var configFactory = new WorkerConfigFactory(config, testLogger);
            var testEnvVariables = new Dictionary<string, string>
            {
                { "JAVA_HOME", string.Empty }
            };
            using (var variables = new TestScopedSettings(scriptSettingsManager, testEnvVariables))
            {
                var javaPath = configFactory.GetExecutablePathForJava("java");
                Assert.Equal("java", javaPath);
            }
        }
    }
}