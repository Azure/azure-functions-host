// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.OutOfProc;
using Microsoft.Azure.WebJobs.Script.OutOfProc.Http;
using Microsoft.Azure.WebJobs.Script.Rpc;
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
        private readonly ScriptSettingsManager _settingsManager;

        private Mock<IScriptEventManager> _mockEventManager = new Mock<IScriptEventManager>();
        private IWorkerProcessFactory _defaultWorkerProcessFactory = new DefaultWorkerProcessFactory();
        private IProcessRegistry _processRegistry = new EmptyProcessRegistry();
        private Mock<ILanguageWorkerConsoleLogSource> _languageWorkerConsoleLogSource = new Mock<ILanguageWorkerConsoleLogSource>();
        private ILogger _testLogger = new TestLogger("test");
        private HttpWorkerOptions _httpWorkerOptions;

        public HttpWorkerProcessTests()
        {
            _httpWorkerOptions = new HttpWorkerOptions();
            _httpWorkerOptions.Port = _workerPort;
            _httpWorkerOptions.Arguments = new WorkerProcessArguments() { ExecutablePath = "test" };
            _settingsManager = ScriptSettingsManager.Instance;
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
                HttpWorkerProcess httpWorkerProcess = new HttpWorkerProcess(_testWorkerId, _rootScriptPath, _httpWorkerOptions, _mockEventManager.Object, _defaultWorkerProcessFactory, _processRegistry, _testLogger, _languageWorkerConsoleLogSource.Object);
                Process childProcess = httpWorkerProcess.CreateWorkerProcess();
                Assert.NotNull(childProcess.StartInfo.EnvironmentVariables);
                Assert.Equal(childProcess.StartInfo.EnvironmentVariables[HttpWorkerConstants.PortEnvVarName], _workerPort.ToString());
                childProcess.Dispose();
            }
        }
    }
}
