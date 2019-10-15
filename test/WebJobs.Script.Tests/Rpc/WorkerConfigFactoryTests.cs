// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.OutOfProc;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{
    public class WorkerConfigFactoryTests : IDisposable
    {
        private TestSystemRuntimeInformation _testSysRuntimeInfo = new TestSystemRuntimeInformation();
        private TestEnvironment _testEnvironment;

        public WorkerConfigFactoryTests()
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
            var expectedWorkersDir = Path.Combine(Path.GetDirectoryName(new Uri(typeof(WorkerConfigFactory).Assembly.CodeBase).LocalPath), LanguageWorkerConstants.DefaultWorkersDirectoryName);
            var config = new ConfigurationBuilder().Build();
            var testLogger = new TestLogger("test");
            var configFactory = new WorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment);
            Assert.Equal(expectedWorkersDir, configFactory.WorkersDirPath);
        }

        [Fact]
        public void LanguageWorker_WorkersDir_Set()
        {
            var expectedWorkersDir = @"d:\testWorkersDir";
            var config = new ConfigurationBuilder()
                   .AddInMemoryCollection(new Dictionary<string, string>
                   {
                       [$"{LanguageWorkerConstants.LanguageWorkersSectionName}:{OutOfProcConstants.WorkersDirectorySectionName}"] = expectedWorkersDir
                   })
                   .Build();
            var testLogger = new TestLogger("test");
            var configFactory = new WorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment);
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
            var configFactory = new WorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment);
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
            var configFactory = new WorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment);
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
            var configFactory = new WorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment);
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
            var configFactory = new WorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment);
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
            var configFactory = new WorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment);
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
            var configFactory = new WorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment);
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
            var configFactory = new WorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment);
            var languageSection = config.GetSection("languageWorkers:java");
            var testEnvVariables = new Dictionary<string, string>
            {
                { "JAVA_OPTS", javaOpts }
            };
            using (var variables = new TestScopedSettings(scriptSettingsManager, testEnvVariables))
            {
                WorkerConfigFactory.AddArgumentsFromAppSettings(workerDescription, languageSection);
                Assert.Equal(2, workerDescription.Arguments.Count);
                Assert.Equal(expectedArgument, workerDescription.Arguments[1]);
            }
        }

        [Theory]
        [InlineData("%FUNCTIONS_WORKER_RUNTIME_VERSION%/{os}/{architecture}", "3.7/LINUX/X64")]
        [InlineData("%FUNCTIONS_WORKER_RUNTIME_VERSION%/{architecture}", "3.7/X64")]
        [InlineData("%FUNCTIONS_WORKER_RUNTIME_VERSION%/{os}", "3.7/LINUX")]
        public void LanguageWorker_HydratedWorkerPath_EnvironmentVersionSet(string defaultWorkerPath, string expectedPath)
        {
            _testEnvironment.SetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeVersionSettingName, "3.7");

            RpcWorkerDescription workerDescription = new RpcWorkerDescription()
            {
                Arguments = new List<string>(),
                DefaultExecutablePath = "python",
                DefaultWorkerPath = defaultWorkerPath,
                DefaultRuntimeVersion = "3.6",
                SupportedArchitectures = new List<string>() { Architecture.X64.ToString(), Architecture.X86.ToString() },
                SupportedRuntimeVersions = new List<string>() { "3.6", "3.7" },
                SupportedOperatingSystems = new List<string>()
                    {
                        OSPlatform.Windows.ToString(),
                        OSPlatform.OSX.ToString(),
                        OSPlatform.Linux.ToString()
                    },
                WorkerDirectory = string.Empty,
                Extensions = new List<string>() { ".py" },
                Language = "python"
            };
            var configBuilder = ScriptSettingsManager.CreateDefaultConfigurationBuilder()
                  .AddInMemoryCollection(new Dictionary<string, string>
                  {
                      ["languageWorker"] = "test"
                  });
            var config = configBuilder.Build();
            var scriptSettingsManager = new ScriptSettingsManager(config);
            var testLogger = new TestLogger("test");
            var configFactory = new WorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment);

            Assert.Equal(expectedPath, configFactory.GetHydratedWorkerPath(workerDescription));
        }

        [Theory]
        [InlineData("%FUNCTIONS_WORKER_RUNTIME_VERSION%/{os}/{architecture}", "3.6/LINUX/X64")]
        [InlineData("%FUNCTIONS_WORKER_RUNTIME_VERSION%/{architecture}", "3.6/X64")]
        [InlineData("%FUNCTIONS_WORKER_RUNTIME_VERSION%/{os}", "3.6/LINUX")]
        public void LanguageWorker_HydratedWorkerPath_EnvironmentVersionNotSet(string defaultWorkerPath, string expectedPath)
        {
            // We fall back to the default version when this is not set
            // Environment.SetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeVersionSettingName, "3.7");

            RpcWorkerDescription workerDescription = new RpcWorkerDescription()
            {
                Arguments = new List<string>(),
                DefaultExecutablePath = "python",
                SupportedArchitectures = new List<string>() { Architecture.X64.ToString(), Architecture.X86.ToString() },
                SupportedRuntimeVersions = new List<string>() { "3.6", "3.7" },
                SupportedOperatingSystems = new List<string>()
                    {
                        OSPlatform.Windows.ToString(),
                        OSPlatform.OSX.ToString(),
                        OSPlatform.Linux.ToString()
                    },
                DefaultWorkerPath = defaultWorkerPath,
                WorkerDirectory = string.Empty,
                Extensions = new List<string>() { ".py" },
                Language = "python",
                DefaultRuntimeVersion = "3.6"
            };
            var configBuilder = ScriptSettingsManager.CreateDefaultConfigurationBuilder()
                  .AddInMemoryCollection(new Dictionary<string, string>
                  {
                      ["languageWorker"] = "test"
                  });
            var config = configBuilder.Build();
            var scriptSettingsManager = new ScriptSettingsManager(config);
            var testLogger = new TestLogger("test");
            var configFactory = new WorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment);

            Assert.Equal(expectedPath, configFactory.GetHydratedWorkerPath(workerDescription));
        }

        [Theory]
        [InlineData(Architecture.Arm)]
        [InlineData(Architecture.Arm64)]
        public void LanguageWorker_HydratedWorkerPath_UnsupportedArchitecture(Architecture unsupportedArch)
        {
            RpcWorkerDescription workerDescription = new RpcWorkerDescription()
            {
                Arguments = new List<string>(),
                DefaultExecutablePath = "python",
                DefaultWorkerPath = "{architecture}/worker.py",
                WorkerDirectory = string.Empty,
                SupportedArchitectures = new List<string>() { Architecture.X64.ToString(), Architecture.X86.ToString() },
                SupportedRuntimeVersions = new List<string>() { "3.6", "3.7" },
                SupportedOperatingSystems = new List<string>()
                    {
                        OSPlatform.Windows.ToString(),
                        OSPlatform.OSX.ToString(),
                        OSPlatform.Linux.ToString()
                    },
                Extensions = new List<string>() { ".py" },
                Language = "python",
                DefaultRuntimeVersion = "3.7"
            };
            var configBuilder = ScriptSettingsManager.CreateDefaultConfigurationBuilder()
                  .AddInMemoryCollection(new Dictionary<string, string>
                  {
                      ["languageWorker"] = "test"
                  });
            var config = configBuilder.Build();
            var scriptSettingsManager = new ScriptSettingsManager(config);
            var testLogger = new TestLogger("test");
            Mock<ISystemRuntimeInformation> mockRuntimeInfo = new Mock<ISystemRuntimeInformation>();
            mockRuntimeInfo.Setup(r => r.GetOSArchitecture()).Returns(unsupportedArch);
            mockRuntimeInfo.Setup(r => r.GetOSPlatform()).Returns(OSPlatform.Linux);
            var configFactory = new WorkerConfigFactory(config, testLogger, mockRuntimeInfo.Object, _testEnvironment);

            var ex = Assert.Throws<PlatformNotSupportedException>(() => configFactory.GetHydratedWorkerPath(workerDescription));
            Assert.Equal(ex.Message, $"Architecture {unsupportedArch.ToString()} is not supported for language {workerDescription.Language}");
        }

        [Fact]
        public void LanguageWorker_HydratedWorkerPath_UnsupportedOS()
        {
            OSPlatform bogusOS = OSPlatform.Create("BogusOS");
            RpcWorkerDescription workerDescription = new RpcWorkerDescription()
            {
                Arguments = new List<string>(),
                DefaultExecutablePath = "python",
                DefaultWorkerPath = "{os}/worker.py",
                SupportedOperatingSystems = new List<string>()
                    {
                        OSPlatform.Windows.ToString(),
                        OSPlatform.OSX.ToString(),
                        OSPlatform.Linux.ToString()
                    },
                WorkerDirectory = string.Empty,
                Extensions = new List<string>() { ".py" },
                Language = "python",
                DefaultRuntimeVersion = "3.7"
            };
            var configBuilder = ScriptSettingsManager.CreateDefaultConfigurationBuilder()
                  .AddInMemoryCollection(new Dictionary<string, string>
                  {
                      ["languageWorker"] = "test"
                  });
            var config = configBuilder.Build();
            var scriptSettingsManager = new ScriptSettingsManager(config);
            var testLogger = new TestLogger("test");
            Mock<ISystemRuntimeInformation> mockRuntimeInfo = new Mock<ISystemRuntimeInformation>();
            mockRuntimeInfo.Setup(r => r.GetOSArchitecture()).Returns(Architecture.X64);
            mockRuntimeInfo.Setup(r => r.GetOSPlatform()).Returns(bogusOS);
            var configFactory = new WorkerConfigFactory(config, testLogger, mockRuntimeInfo.Object, _testEnvironment);

            var ex = Assert.Throws<PlatformNotSupportedException>(() => configFactory.GetHydratedWorkerPath(workerDescription));
            Assert.Equal(ex.Message, $"OS BogusOS is not supported for language {workerDescription.Language}");
        }

        [Fact]
        public void LanguageWorker_HydratedWorkerPath_UnsupportedDefaultRuntimeVersion()
        {
            RpcWorkerDescription workerDescription = new RpcWorkerDescription()
            {
                Arguments = new List<string>(),
                DefaultExecutablePath = "python",
                SupportedRuntimeVersions = new List<string>() { "3.6", "3.7" },
                DefaultWorkerPath = $"{LanguageWorkerConstants.RuntimeVersionPlaceholder}/worker.py",
                WorkerDirectory = string.Empty,
                Extensions = new List<string>() { ".py" },
                Language = "python",
                DefaultRuntimeVersion = "3.5"
            };
            var configBuilder = ScriptSettingsManager.CreateDefaultConfigurationBuilder()
                  .AddInMemoryCollection(new Dictionary<string, string>
                  {
                      ["languageWorker"] = "test"
                  });
            var config = configBuilder.Build();
            var scriptSettingsManager = new ScriptSettingsManager(config);
            var testLogger = new TestLogger("test");
            var configFactory = new WorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment);

            var ex = Assert.Throws<NotSupportedException>(() => configFactory.GetHydratedWorkerPath(workerDescription));
            Assert.Equal(ex.Message, $"Version {workerDescription.DefaultRuntimeVersion} is not supported for language {workerDescription.Language}");
        }

        [Fact]
        public void LanguageWorker_HydratedWorkerPath_UnsupportedEnvironmentRuntimeVersion()
        {
            _testEnvironment.SetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeVersionSettingName, "3.4");

            RpcWorkerDescription workerDescription = new RpcWorkerDescription()
            {
                Arguments = new List<string>(),
                DefaultExecutablePath = "python",
                SupportedRuntimeVersions = new List<string>() { "3.6", "3.7" },
                DefaultWorkerPath = $"{LanguageWorkerConstants.RuntimeVersionPlaceholder}/worker.py",
                WorkerDirectory = string.Empty,
                Extensions = new List<string>() { ".py" },
                Language = "python",
                DefaultRuntimeVersion = "3.7" // Ignore this if environment is set
            };
            var configBuilder = ScriptSettingsManager.CreateDefaultConfigurationBuilder()
                  .AddInMemoryCollection(new Dictionary<string, string>
                  {
                      ["languageWorker"] = "test"
                  });
            var config = configBuilder.Build();
            var scriptSettingsManager = new ScriptSettingsManager(config);
            var testLogger = new TestLogger("test");
            var configFactory = new WorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment);

            var ex = Assert.Throws<NotSupportedException>(() => configFactory.GetHydratedWorkerPath(workerDescription));
            Assert.Equal(ex.Message, $"Version 3.4 is not supported for language {workerDescription.Language}");
        }
    }
}