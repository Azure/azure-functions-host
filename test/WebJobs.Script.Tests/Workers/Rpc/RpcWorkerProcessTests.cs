// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class RpcWorkerProcessTests
    {
        private readonly Mock<IWorkerProcessFactory> _workerProcessFactory = new Mock<IWorkerProcessFactory>();
        private readonly Mock<IScriptEventManager> _eventManager = new Mock<IScriptEventManager>();
        private readonly TestLogger _logger = new TestLogger("test");
        private readonly IOptions<FunctionsHostingConfigOptions> _functionsHostingConfigOptions;
        private readonly Mock<IProcessRegistry> _processRegistry = new Mock<IProcessRegistry>();
        private readonly TestRpcServer _rpcServer = new TestRpcServer();
        private readonly Mock<IWorkerConsoleLogSource> _languageWorkerConsoleLogSource = new Mock<IWorkerConsoleLogSource>();
        private readonly TestEnvironment _testEnv = new TestEnvironment();
        private readonly Mock<IServiceProvider> _serviceProviderMock = new Mock<IServiceProvider>(MockBehavior.Strict);
        private readonly TestOptionsMonitor<ScriptApplicationHostOptions> _scriptApplicationHostOptions = new TestOptionsMonitor<ScriptApplicationHostOptions>();
        private Mock<IHostProcessMonitor> _hostProcessMonitorMock = new Mock<IHostProcessMonitor>(MockBehavior.Strict);

        public RpcWorkerProcessTests()
        {
            var scriptHostManagerMock = new Mock<IScriptHostManager>(MockBehavior.Strict);
            var scriptHostServiceProviderMock = scriptHostManagerMock.As<IServiceProvider>();
            scriptHostServiceProviderMock.Setup(p => p.GetService(typeof(IHostProcessMonitor))).Returns(() => _hostProcessMonitorMock.Object);
            _serviceProviderMock.Setup(p => p.GetService(typeof(IScriptHostManager))).Returns(scriptHostManagerMock.Object);
            _functionsHostingConfigOptions = Options.Create(new FunctionsHostingConfigOptions());
        }

        private RpcWorkerProcess GetRpcWorkerConfigProcess(RpcWorkerConfig workerConfig)
        {
            return new RpcWorkerProcess("node",
                "testworkerId",
                "testrootPath",
                _rpcServer.Uri,
                workerConfig,
                _eventManager.Object,
                _workerProcessFactory.Object,
                _processRegistry.Object,
                _logger,
                _languageWorkerConsoleLogSource.Object,
                new TestMetricsLogger(),
                _serviceProviderMock.Object,
                _functionsHostingConfigOptions,
                _testEnv,
                _scriptApplicationHostOptions,
                new LoggerFactory());
        }

        [Fact]
        public void Constructor_RegistersHostStartedEvent()
        {
            GetRpcWorkerConfigProcess(TestHelpers.GetTestWorkerConfigs().ElementAt(0));
            Func<IObserver<ScriptEvent>, bool> validate = p =>
            {
                // validate the internal reactive ISink<HostStartEvent> implementation that
                // results from the Subscribe extension method
                Type eventType = p.GetType().GetInterfaces()[0].GetGenericArguments()[0];
                return eventType == typeof(HostStartEvent);
            };

            _eventManager.Verify(_ => _.Subscribe(It.Is<IObserver<ScriptEvent>>(p => validate(p))), Times.Once());
        }

        [Fact]
        public void OnHostStart_RegistersProcessWithMonitor()
        {
            var rpcWorkerProcess = GetRpcWorkerConfigProcess(TestHelpers.GetTestWorkerConfigs().ElementAt(0));
            Process process = Process.GetCurrentProcess();
            rpcWorkerProcess.Process = process;

            _hostProcessMonitorMock.Setup(p => p.RegisterChildProcess(process));

            HostStartEvent evt = new HostStartEvent();
            rpcWorkerProcess.OnHostStart(evt);
            _hostProcessMonitorMock.Verify(p => p.RegisterChildProcess(process), Times.Once);
        }

        [Fact]
        public void RegisterWithProcessMonitor_Succeeds()
        {
            var rpcWorkerProcess = GetRpcWorkerConfigProcess(TestHelpers.GetTestWorkerConfigs().ElementAt(0));
            Process process = Process.GetCurrentProcess();
            rpcWorkerProcess.Process = process;

            _hostProcessMonitorMock.Setup(p => p.RegisterChildProcess(process));

            rpcWorkerProcess.RegisterWithProcessMonitor();
            _hostProcessMonitorMock.Verify(p => p.RegisterChildProcess(process), Times.Once);

            // registration is skipped if attempted again for the same monitor
            rpcWorkerProcess.RegisterWithProcessMonitor();
            _hostProcessMonitorMock.Verify(p => p.RegisterChildProcess(process), Times.Once);

            // if the monitor changes (e.g. due to a host restart)
            // registration is performed
            _hostProcessMonitorMock = new Mock<IHostProcessMonitor>(MockBehavior.Strict);
            _hostProcessMonitorMock.Setup(p => p.RegisterChildProcess(process));
            rpcWorkerProcess.RegisterWithProcessMonitor();
            _hostProcessMonitorMock.Verify(p => p.RegisterChildProcess(process), Times.Once);
        }

        [Fact]
        public void UnregisterFromProcessMonitor_Succeeds()
        {
            var rpcWorkerProcess = GetRpcWorkerConfigProcess(TestHelpers.GetTestWorkerConfigs().ElementAt(0));
            Process process = Process.GetCurrentProcess();
            rpcWorkerProcess.Process = process;

            _hostProcessMonitorMock.Setup(p => p.RegisterChildProcess(process));
            _hostProcessMonitorMock.Setup(p => p.UnregisterChildProcess(process));

            // not yet registered so noop
            rpcWorkerProcess.UnregisterFromProcessMonitor();
            _hostProcessMonitorMock.Verify(p => p.UnregisterChildProcess(process), Times.Never);

            rpcWorkerProcess.RegisterWithProcessMonitor();
            rpcWorkerProcess.UnregisterFromProcessMonitor();
            _hostProcessMonitorMock.Verify(p => p.UnregisterChildProcess(process), Times.Once);

            // attempting to unregister again is a noop
            rpcWorkerProcess.UnregisterFromProcessMonitor();
            _hostProcessMonitorMock.Verify(p => p.UnregisterChildProcess(process), Times.Once);
        }

        [Fact]
        public void ErrorMessageQueue_Empty()
        {
            var rpcWorkerProcess = GetRpcWorkerConfigProcess(TestHelpers.GetTestWorkerConfigs().ElementAt(0));
            Assert.Empty(rpcWorkerProcess.ProcessStdErrDataQueue);
        }

        [Fact]
        public void ErrorMessageQueue_Enqueue_Success()
        {
            var rpcWorkerProcess = GetRpcWorkerConfigProcess(TestHelpers.GetTestWorkerConfigs().ElementAt(0));
            WorkerProcessUtilities.AddStdErrMessage(rpcWorkerProcess.ProcessStdErrDataQueue, "Error1");
            WorkerProcessUtilities.AddStdErrMessage(rpcWorkerProcess.ProcessStdErrDataQueue, "Error2");

            Assert.True(rpcWorkerProcess.ProcessStdErrDataQueue.Count == 2);
            string exceptionMessage = string.Join(",", rpcWorkerProcess.ProcessStdErrDataQueue.Where(s => !string.IsNullOrEmpty(s)));
            Assert.Equal("Error1,Error2", exceptionMessage);
        }

        [Fact]
        public void ErrorMessageQueue_Full_Enqueue_Success()
        {
            var rpcWorkerProcess = GetRpcWorkerConfigProcess(TestHelpers.GetTestWorkerConfigs().ElementAt(0));
            WorkerProcessUtilities.AddStdErrMessage(rpcWorkerProcess.ProcessStdErrDataQueue, "Error1");
            WorkerProcessUtilities.AddStdErrMessage(rpcWorkerProcess.ProcessStdErrDataQueue, "Error2");
            WorkerProcessUtilities.AddStdErrMessage(rpcWorkerProcess.ProcessStdErrDataQueue, "Error3");
            WorkerProcessUtilities.AddStdErrMessage(rpcWorkerProcess.ProcessStdErrDataQueue, "Error4");
            Assert.True(rpcWorkerProcess.ProcessStdErrDataQueue.Count == 3);
            string exceptionMessage = string.Join(",", rpcWorkerProcess.ProcessStdErrDataQueue.Where(s => !string.IsNullOrEmpty(s)));
            Assert.Equal("Error2,Error3,Error4", exceptionMessage);
        }

        [Theory]
        [InlineData("languageWorkerConsoleLog Connection established")]
        [InlineData("LANGUAGEWORKERCONSOLELOG Connection established")]
        [InlineData("LanguageWorkerConsoleLog Connection established")]
        public void IsLanguageWorkerConsoleLog_Returns_True_RemovesLogPrefix(string msg)
        {
            Assert.True(WorkerProcessUtilities.IsConsoleLog(msg));
            Assert.Equal(" Connection established", WorkerProcessUtilities.RemoveLogPrefix(msg));
        }

        [Theory]
        [InlineData("grpc languageWorkerConsoleLog Connection established")]
        [InlineData("My secret languageWorkerConsoleLog")]
        [InlineData("Connection established")]
        public void IsLanguageWorkerConsoleLog_Returns_False(string msg)
        {
            Assert.False(WorkerProcessUtilities.IsConsoleLog(msg));
        }

        [Fact]
        public void HandleWorkerProcessExitError_PublishesWorkerRestartEvent_OnIntentionalRestartExitCode()
        {
            var rpcWorkerProcess = GetRpcWorkerConfigProcess(TestHelpers.GetTestWorkerConfigs().ElementAt(0));
            rpcWorkerProcess.HandleWorkerProcessRestart();

            _eventManager.Verify(_ => _.Publish(It.IsAny<WorkerRestartEvent>()), Times.Once());
            _eventManager.Verify(_ => _.Publish(It.IsAny<WorkerErrorEvent>()), Times.Never());
        }

        [Fact]
        public void WorkerProcess_Dispose()
        {
            var rpcWorkerProcess = GetRpcWorkerConfigProcess(TestHelpers.GetTestWorkerConfigs().ElementAt(0));
            Process process = new Process();
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = $"ls";
            process.StartInfo = startInfo;
            process.Start();

            rpcWorkerProcess.Process = process;
            rpcWorkerProcess.Dispose();
            var traces = _logger.GetLogMessages();
            var disposeLogs = traces.Where(m => string.Equals(m.FormattedMessage, "Worker process has not exited despite waiting for 1000 ms"));
            Assert.False(disposeLogs.Any());
        }

        [Fact]
        public void CreateWorkerProcess_AddsHostingConfiguration()
        {
            var rpcWorkerProcess = GetRpcWorkerConfigProcess(TestHelpers.GetTestWorkerConfigs().ElementAt(0));
            _functionsHostingConfigOptions.Value.Features["feature1"] = "1";
            var process = rpcWorkerProcess.CreateWorkerProcess();

            _workerProcessFactory.Verify(x => x.CreateWorkerProcess(It.Is<WorkerContext>(c => c.EnvironmentVariables["feature1"] == "1")));
        }

        [Fact]
        public void CreateWorkerProcess_AddsExecutableWorkingDirectory()
        {
            var rpcWorkerProcess = GetRpcWorkerConfigProcess(TestHelpers.GetTestWorkerConfigsWithExecutableWorkingDirectory().First());
            var process = rpcWorkerProcess.CreateWorkerProcess();

            _workerProcessFactory.Verify(x => x.CreateWorkerProcess(It.Is<WorkerContext>(c => c.WorkingDirectory == "executableDirectory")));
        }

        [Fact]
        public void WorkerProcess_WaitForExit_AfterExit_DoesNotThrow()
        {
            var rpcWorkerProcess = GetRpcWorkerConfigProcess(TestHelpers.GetTestWorkerConfigsWithExecutableWorkingDirectory().ElementAt(0));
            Process process = new Process();
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = $"ls";
            process.StartInfo = startInfo;
            process.Start();

            rpcWorkerProcess.Process = process;
            rpcWorkerProcess.Dispose();

            rpcWorkerProcess.WaitForProcessExitInMilliSeconds(1);

            var traces = _logger.GetLogMessages();
            string msg = traces.Single().FormattedMessage;
            Assert.StartsWith("An exception was thrown while waiting for a worker process to exit.", msg);

            Exception ex = traces.Single().Exception;
            Assert.IsType<InvalidOperationException>(ex);
        }
    }
}