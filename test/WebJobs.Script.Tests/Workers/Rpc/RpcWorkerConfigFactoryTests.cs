// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class RpcWorkerConfigFactoryTests : IDisposable
    {
        private TestSystemRuntimeInformation _testSysRuntimeInfo = new TestSystemRuntimeInformation();
        private TestEnvironment _testEnvironment;

        public RpcWorkerConfigFactoryTests()
        {
            _testEnvironment = new TestEnvironment();
        }

        public void Dispose()
        {
            _testEnvironment.Clear();
        }

        [Fact]
        public void DefaultLanguageWorkersDir()
        {
            var expectedWorkersDir = Path.Combine(Path.GetDirectoryName(new Uri(typeof(RpcWorkerConfigFactory).Assembly.CodeBase).LocalPath), RpcWorkerConstants.DefaultWorkersDirectoryName);
            var config = new ConfigurationBuilder().Build();
            var testLogger = new TestLogger("test");
            var configFactory = new RpcWorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment, new TestMetricsLogger());
            Assert.Equal(expectedWorkersDir, configFactory.WorkersDirPath);
        }

        [Fact]
        public void LanguageWorker_WorkersDir_Set()
        {
            var expectedWorkersDir = @"d:\testWorkersDir";
            var config = new ConfigurationBuilder()
                   .AddInMemoryCollection(new Dictionary<string, string>
                   {
                       [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{WorkerConstants.WorkersDirectorySectionName}"] = expectedWorkersDir
                   })
                   .Build();
            var testLogger = new TestLogger("test");
            var configFactory = new RpcWorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment, new TestMetricsLogger());
            Assert.Equal(expectedWorkersDir, configFactory.WorkersDirPath);
        }

        [Fact]
        public void LanguageWorker_WorkersDir_NotSet()
        {
            var expectedWorkersDir = Path.Combine(Path.GetDirectoryName(new Uri(typeof(RpcWorkerConfigFactory).Assembly.CodeBase).LocalPath), RpcWorkerConstants.DefaultWorkersDirectoryName);
            var configBuilder = ScriptSettingsManager.CreateDefaultConfigurationBuilder()
                   .AddInMemoryCollection(new Dictionary<string, string>
                   {
                       ["languageWorker"] = "test"
                   });
            var config = configBuilder.Build();
            var scriptSettingsManager = new ScriptSettingsManager(config);
            var testLogger = new TestLogger("test");
            var configFactory = new RpcWorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment, new TestMetricsLogger());
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
            var configFactory = new RpcWorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment, new TestMetricsLogger());
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
        public void JavaPath_AppServiceEnv_JavaHomeSet_AppServiceEnvOverrides()
        {
            var configBuilder = ScriptSettingsManager.CreateDefaultConfigurationBuilder()
                  .AddInMemoryCollection(new Dictionary<string, string>
                  {
                      ["languageWorker"] = "test"
                  });
            var config = configBuilder.Build();
            var scriptSettingsManager = new ScriptSettingsManager(config);
            var testLogger = new TestLogger("test");
            var configFactory = new RpcWorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment, new TestMetricsLogger());
            var testEnvVariables = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsiteInstanceId, "123" },
                { "JAVA_HOME", @"D:\Program Files\Java\zulu8.31.0.2-jre8.0.181-win_x64" }
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
            var configFactory = new RpcWorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment, new TestMetricsLogger());
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
            var configFactory = new RpcWorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment, new TestMetricsLogger());
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
            var configFactory = new RpcWorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment, new TestMetricsLogger());
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

        [Theory]
        [InlineData("-Djava.net.preferIPv4Stack = true", "-Djava.net.preferIPv4Stack = true")]
        [InlineData("-agentlib:jdwp=transport=dt_socket,server=y,suspend=n,address=5005", "")]
        public void AddArgumentsFromAppSettings_JavaOpts(string expectedArgument, string javaOpts)
        {
            RpcWorkerDescription workerDescription = new RpcWorkerDescription()
            {
                Arguments = new List<string>() { "-jar" },
                DefaultExecutablePath = "java",
                DefaultWorkerPath = "javaworker.jar",
                Extensions = new List<string>() { ".jar" },
                Language = "java"
            };
            var configBuilder = ScriptSettingsManager.CreateDefaultConfigurationBuilder()
                  .AddInMemoryCollection(new Dictionary<string, string>
                  {
                      ["languageWorker"] = "test",
                      ["languageWorkers:java:arguments"] = "-agentlib:jdwp=transport=dt_socket,server=y,suspend=n,address=5005"
                  });
            var config = configBuilder.Build();
            var scriptSettingsManager = new ScriptSettingsManager(config);
            var testLogger = new TestLogger("test");
            var languageSection = config.GetSection("languageWorkers:java");
            var testEnvVariables = new Dictionary<string, string>
            {
                { "JAVA_OPTS", javaOpts }
            };
            using (var variables = new TestScopedSettings(scriptSettingsManager, testEnvVariables))
            {
                RpcWorkerConfigFactory.AddArgumentsFromAppSettings(workerDescription, languageSection);
                Assert.Equal(2, workerDescription.Arguments.Count);
                Assert.Equal(expectedArgument, workerDescription.Arguments[1]);
            }
        }

        [Theory]
        [InlineData("python", "Python", true)]
        [InlineData("python", "NOde", false)]
        [InlineData("python", "", true)]
        [InlineData("python", null, true)]
        public void ShouldAddProvider_Returns_Expected(string workerLanguage, string workerRuntime, bool expectedResult)
        {
            var expectedWorkersDir = Path.Combine(Path.GetDirectoryName(new Uri(typeof(RpcWorkerConfigFactory).Assembly.CodeBase).LocalPath), RpcWorkerConstants.DefaultWorkersDirectoryName);
            var config = new ConfigurationBuilder().Build();
            var testLogger = new TestLogger("test");
            RpcWorkerConfigFactory rpcWorkerConfigFactory = new RpcWorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment, new TestMetricsLogger());
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, workerRuntime);
            Assert.Equal(expectedResult, rpcWorkerConfigFactory.ShouldAddWorkerConfig(workerLanguage));
        }
    }
}