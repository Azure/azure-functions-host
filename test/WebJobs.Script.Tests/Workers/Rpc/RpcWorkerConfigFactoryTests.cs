﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public void GetDefaultWorkersDirectory_Returns_Expected()
        {
            string assemblyLocalPath = Path.GetDirectoryName(new Uri(typeof(RpcWorkerConfigFactory).Assembly.CodeBase).LocalPath);
            string defaultWorkersDirPath = Path.Combine(assemblyLocalPath, RpcWorkerConstants.DefaultWorkersDirectoryName);
            Func<string, bool> testDirectoryExists = path =>
            {
                return false;
            };
            var expectedWorkersDirIsCurrentDir = Path.Combine(assemblyLocalPath, RpcWorkerConstants.DefaultWorkersDirectoryName);
            var expectedWorkersDirIsParentDir = Path.Combine(Directory.GetParent(assemblyLocalPath).FullName, RpcWorkerConstants.DefaultWorkersDirectoryName);
            var config = new ConfigurationBuilder().Build();
            var testLogger = new TestLogger("test");

            Assert.Equal(expectedWorkersDirIsCurrentDir, RpcWorkerConfigFactory.GetDefaultWorkersDirectory(Directory.Exists));
            Assert.Equal(expectedWorkersDirIsParentDir, RpcWorkerConfigFactory.GetDefaultWorkersDirectory(testDirectoryExists));
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

        [Theory]
        [InlineData(@"D:\Program Files\Java\jdk1.7.0_51")]
        [InlineData(null)]
        public void JavaPath_AppServiceEnv(string javaHomePath)
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
                { "JAVA_HOME",  javaHomePath }
            };
            using (var variables = new TestScopedSettings(scriptSettingsManager, testEnvVariables))
            {
                var workerConfigs = configFactory.GetConfigs();
                var javaPath = workerConfigs.Where(c => c.Description.Language.Equals("java", StringComparison.OrdinalIgnoreCase)).FirstOrDefault().Description.DefaultExecutablePath;
                if (string.IsNullOrEmpty(javaHomePath))
                {
                    Assert.Equal(@"%JAVA_HOME%/bin/java", javaPath);
                }
                else
                {
                    Assert.Equal(@"D:\Program Files\Java\zulu8.23.0.3-jdk8.0.144-win_x64/bin/java", javaPath);
                }
            }
        }

        [Fact]
        public void DefaultWorkerConfigs_Overrides_DefaultWorkerRuntimeVersion_AppSetting()
        {
            var configBuilder = ScriptSettingsManager.CreateDefaultConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["languageWorkers:python:defaultRuntimeVersion"] = "3.8"
                });
            var config = configBuilder.Build();
            var scriptSettingsManager = new ScriptSettingsManager(config);
            var testLogger = new TestLogger("test");
            var testEnvVariables = new Dictionary<string, string>
            {
                { "languageWorkers:python:defaultRuntimeVersion", "3.8" }
            };
            using (var variables = new TestScopedSettings(scriptSettingsManager, testEnvVariables))
            {
                var configFactory = new RpcWorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment, new TestMetricsLogger());
                var workerConfigs = configFactory.GetConfigs();
                var pythonWorkerConfig = workerConfigs.Where(w => w.Description.Language.Equals("python", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                Assert.Equal(4, workerConfigs.Count);
                Assert.NotNull(pythonWorkerConfig);
                Assert.Equal("3.8", pythonWorkerConfig.Description.DefaultRuntimeVersion);
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
        [InlineData("python", "Python", false, true)]
        [InlineData("python", "NOde", false, false)]
        [InlineData("python", "", false, true)]
        [InlineData("python", null, false, true)]
        [InlineData("python", "NOde", true, true)]
        [InlineData("python", null, true, true)]
        public void ShouldAddProvider_Returns_Expected(string workerLanguage, string workerRuntime, bool placeholderMode, bool expectedResult)
        {
            if (placeholderMode)
            {
                _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            }
            var expectedWorkersDir = Path.Combine(Path.GetDirectoryName(new Uri(typeof(RpcWorkerConfigFactory).Assembly.CodeBase).LocalPath), RpcWorkerConstants.DefaultWorkersDirectoryName);
            var config = new ConfigurationBuilder().Build();
            var testLogger = new TestLogger("test");
            RpcWorkerConfigFactory rpcWorkerConfigFactory = new RpcWorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment, new TestMetricsLogger());
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, workerRuntime);
            Assert.Equal(expectedResult, rpcWorkerConfigFactory.ShouldAddWorkerConfig(workerLanguage));
        }
    }
}