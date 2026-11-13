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
using WebJobs.Script.Tests;
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

        [Theory]
        [InlineData("node")]
        [InlineData("python")]
        [InlineData("java")]
        public void CreateWorkerProcess_WebFunction_PassesAssignedEndpoint(string runtime)
        {
            WebFunctionLanguage language = WebFunctionWorkerDescriptionResolver.GetConfiguredLanguage(runtime);
            _httpWorkerOptions.IsWebFunction = true;
            _httpWorkerOptions.WebFunctionLanguage = language;

            HttpWorkerProcess worker = new(_testWorkerId, _rootScriptPath, _httpWorkerOptions, _mockEventManager.Object, _defaultWorkerProcessFactory, _processRegistry, _testLogger, _languageWorkerConsoleLogSource.Object, new TestEnvironment(), new TestMetricsLogger(), _serviceProviderMock.Object, _scriptApplicationHostOptions, new LoggerFactory());
            using Process process = worker.CreateWorkerProcess();

            Assert.Equal("127.0.0.1:8090", process.StartInfo.EnvironmentVariables[HttpWorkerConstants.WebFunctionEndpointEnvVarName]);
            Assert.Equal("8090", process.StartInfo.EnvironmentVariables["PORT"]);
            if (language is WebFunctionLanguage.Java)
            {
                Assert.Equal("8090", process.StartInfo.EnvironmentVariables["SERVER_PORT"]);
                Assert.Equal("127.0.0.1", process.StartInfo.EnvironmentVariables["SERVER_ADDRESS"]);
            }
        }

        [Fact]
        public void CreateWorkerProcess_CustomHandler_PreservesExistingPortEnvironment()
        {
            using TestScopedSettings settings = new(_settingsManager, "PORT", "1234");
            HttpWorkerProcess worker = new(_testWorkerId, _rootScriptPath, _httpWorkerOptions, _mockEventManager.Object, _defaultWorkerProcessFactory, _processRegistry, _testLogger, _languageWorkerConsoleLogSource.Object, new TestEnvironment(), new TestMetricsLogger(), _serviceProviderMock.Object, _scriptApplicationHostOptions, new LoggerFactory());

            using Process process = worker.CreateWorkerProcess();

            Assert.Equal("1234", process.StartInfo.EnvironmentVariables["PORT"]);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("existing-python-path")]
        public void CreateWorkerProcess_PythonWebFunction_PreservesPythonPath(string existingPythonPath)
        {
            using TempDirectory app = new();
            string packagesPath = Path.Combine(app.Path, ".python_packages", "lib", "site-packages");
            Directory.CreateDirectory(packagesPath);
            using TestScopedSettings settings = new(_settingsManager, "PYTHONPATH", existingPythonPath);
            _httpWorkerOptions.IsWebFunction = true;
            _httpWorkerOptions.WebFunctionLanguage = WebFunctionLanguage.Python;
            _httpWorkerOptions.Description.WorkingDirectory = app.Path;

            HttpWorkerProcess worker = new(_testWorkerId, app.Path, _httpWorkerOptions, _mockEventManager.Object, _defaultWorkerProcessFactory, _processRegistry, _testLogger, _languageWorkerConsoleLogSource.Object, new TestEnvironment(), new TestMetricsLogger(), _serviceProviderMock.Object, _scriptApplicationHostOptions, new LoggerFactory());
            using Process process = worker.CreateWorkerProcess();

            string expected = existingPythonPath is null
                ? packagesPath
                : $"{packagesPath}{Path.PathSeparator}{existingPythonPath}";
            Assert.Equal(expected, process.StartInfo.EnvironmentVariables["PYTHONPATH"]);
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