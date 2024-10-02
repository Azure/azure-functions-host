// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Http
{
    public class HttpWorkerProcessTests
    {
        private const string _testWorkerId = "testId";
        private const string _rootScriptPath = "c:\\testDir";
        private const int _workerPort = 8090;

        private readonly string _executablePath = System.IO.Path.GetTempFileName();
        private readonly ScriptSettingsManager _settingsManager;
        private readonly Mock<IScriptEventManager> _mockEventManager = new Mock<IScriptEventManager>();
        private readonly IWorkerProcessFactory _defaultWorkerProcessFactory = new DefaultWorkerProcessFactory(new TestEnvironment(), new LoggerFactory());
        private readonly IProcessRegistry _processRegistry = new EmptyProcessRegistry();
        private readonly Mock<IWorkerConsoleLogSource> _languageWorkerConsoleLogSource = new Mock<IWorkerConsoleLogSource>();
        private readonly TestLogger _testLogger = new TestLogger("test");
        private readonly HttpWorkerOptions _httpWorkerOptions;
        private readonly Mock<IServiceProvider> _serviceProviderMock;
        private readonly TestOptionsMonitor<ScriptApplicationHostOptions> _scriptApplicationHostOptions = new();

        public HttpWorkerProcessTests()
        {
            _httpWorkerOptions = new HttpWorkerOptions()
            {
                Port = _workerPort,
                Arguments = new WorkerProcessArguments() { ExecutablePath = _executablePath },
                Description = new HttpWorkerDescription()
                {
                    WorkingDirectory = @"c:\testDir"
                }
            };
            _settingsManager = ScriptSettingsManager.Instance;
            _serviceProviderMock = new Mock<IServiceProvider>(MockBehavior.Strict);
        }

        [Theory]
        [InlineData("9000")]
        [InlineData("")]
        [InlineData(null)]
        public void CreateWorkerProcess_VerifyEnvVars(string processEnvValue)
        {
            using (new TestScopedSettings(_settingsManager, HttpWorkerConstants.PortEnvVarName, processEnvValue))
            {
                if (!string.IsNullOrEmpty(processEnvValue))
                {
                    Assert.Equal(Environment.GetEnvironmentVariable(HttpWorkerConstants.PortEnvVarName), processEnvValue);
                }
                HttpWorkerProcess httpWorkerProcess = new HttpWorkerProcess(_testWorkerId, _rootScriptPath, _httpWorkerOptions, _mockEventManager.Object, _defaultWorkerProcessFactory, _processRegistry, _testLogger, _languageWorkerConsoleLogSource.Object, new TestEnvironment(), new TestMetricsLogger(), _serviceProviderMock.Object, _scriptApplicationHostOptions, new LoggerFactory());
                Process childProcess = httpWorkerProcess.CreateWorkerProcess();
                Assert.NotNull(childProcess.StartInfo.EnvironmentVariables);
                Assert.Equal(childProcess.StartInfo.EnvironmentVariables[HttpWorkerConstants.PortEnvVarName], _workerPort.ToString());
                Assert.Equal(childProcess.StartInfo.EnvironmentVariables[HttpWorkerConstants.WorkerIdEnvVarName], _testWorkerId);
                Assert.Equal(childProcess.StartInfo.EnvironmentVariables[HttpWorkerConstants.CustomHandlerPortEnvVarName], _workerPort.ToString());
                Assert.Equal(childProcess.StartInfo.EnvironmentVariables[HttpWorkerConstants.CustomHandlerWorkerIdEnvVarName], _testWorkerId);
                childProcess.Dispose();
            }
        }

        [Fact]
        public async Task StartProcess_LinuxConsumption_TriesToAssignExecutePermissions_Exists()
        {
            try
            {
                File.Create(_executablePath).Dispose();
                TestEnvironment testEnvironment = new TestEnvironment();
                testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, "TestContainer");
                var mockHttpWorkerProcess = new HttpWorkerProcess(_testWorkerId, _rootScriptPath, _httpWorkerOptions, _mockEventManager.Object, _defaultWorkerProcessFactory, _processRegistry, _testLogger, _languageWorkerConsoleLogSource.Object, testEnvironment, new TestMetricsLogger(), _serviceProviderMock.Object, _scriptApplicationHostOptions, new LoggerFactory());

                try
                {
                    await mockHttpWorkerProcess.StartProcessAsync();
                }
                catch
                {
                    // expected to throw. Just verifying a log statement occurred before then.
                }

                // Verify method invocation
                var testLogs = _testLogger.GetLogMessages();
                Assert.Contains("Error while assigning execute permission", testLogs[0].FormattedMessage);
            }
            finally
            {
                File.Delete(_executablePath);
            }
        }

        [Fact]
        public async Task StartProcess_LinuxConsumption_TriesToAssignExecutePermissions_NotExists()
        {
            File.Delete(_executablePath);
            TestEnvironment testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, "TestContainer");
            var mockHttpWorkerProcess = new HttpWorkerProcess(_testWorkerId, _rootScriptPath, _httpWorkerOptions, _mockEventManager.Object, _defaultWorkerProcessFactory, _processRegistry, _testLogger, _languageWorkerConsoleLogSource.Object, testEnvironment, new TestMetricsLogger(), _serviceProviderMock.Object, _scriptApplicationHostOptions, new LoggerFactory());

            try
            {
                await mockHttpWorkerProcess.StartProcessAsync();
            }
            catch
            {
                // expected to throw. Just verifying a log statement occurred before then.
            }

            // Verify method invocation
            var testLogs = _testLogger.GetLogMessages();
            Assert.Contains("File path does not exist, not assigning permissions", testLogs[0].FormattedMessage);
        }

        [Theory]
        [InlineData("AccountKey=abcde==", true)]
        [InlineData("teststring", false)]
        public async Task StartProcess_VerifySanitizedCredentialLogging(string input, bool isSecret)
        {
            try
            {
                _httpWorkerOptions.Arguments.WorkerArguments.Add(input);

                File.Create(_executablePath).Dispose();
                TestEnvironment testEnvironment = new TestEnvironment();
                testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, "TestContainer");
                var mockHttpWorkerProcess = new HttpWorkerProcess(_testWorkerId, _rootScriptPath, _httpWorkerOptions, _mockEventManager.Object, _defaultWorkerProcessFactory, _processRegistry, _testLogger, _languageWorkerConsoleLogSource.Object, testEnvironment, new TestMetricsLogger(), _serviceProviderMock.Object, _scriptApplicationHostOptions, new LoggerFactory());

                try
                {
                    await mockHttpWorkerProcess.StartProcessAsync();
                }
                catch
                {
                    // expected to throw. Just verifying a log statement occurred before then.
                }

                // Verify
                var testLogs = _testLogger.GetLogMessages();

                if (isSecret)
                {
                    Assert.DoesNotContain(input, testLogs[1].FormattedMessage);
                    Assert.Contains($"Arguments: [Hidden Credential]", testLogs[1].FormattedMessage);
                }
                else
                {
                    Assert.Contains($"Arguments: {input}", testLogs[1].FormattedMessage);
                }
            }
            finally
            {
                File.Delete(_executablePath);
            }
        }
    }
}