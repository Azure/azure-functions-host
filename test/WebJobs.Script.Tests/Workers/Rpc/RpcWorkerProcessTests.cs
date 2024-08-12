// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
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
        private readonly Mock<IWorkerProcessFactory> _workerProcessFactory;
        private RpcWorkerProcess _rpcWorkerProcess;
        private Mock<IScriptEventManager> _eventManager;
        private Mock<IHostProcessMonitor> _hostProcessMonitorMock;
        private TestLogger _logger = new TestLogger("test");
        private IOptions<FunctionsHostingConfigOptions> _functionsHostingConfigOptions;

        public RpcWorkerProcessTests()
        {
            _eventManager = new Mock<IScriptEventManager>();
            _workerProcessFactory = new Mock<IWorkerProcessFactory>();
            var processRegistry = new Mock<IProcessRegistry>();
            var rpcServer = new TestRpcServer();
            var languageWorkerConsoleLogSource = new Mock<IWorkerConsoleLogSource>();
            var testEnv = new TestEnvironment();
            var testWorkerConfigs = TestHelpers.GetTestWorkerConfigs();

            _hostProcessMonitorMock = new Mock<IHostProcessMonitor>(MockBehavior.Strict);
            var scriptHostManagerMock = new Mock<IScriptHostManager>(MockBehavior.Strict);
            var scriptHostServiceProviderMock = scriptHostManagerMock.As<IServiceProvider>();
            scriptHostServiceProviderMock.Setup(p => p.GetService(typeof(IHostProcessMonitor))).Returns(() => _hostProcessMonitorMock.Object);

            var serviceProviderMock = new Mock<IServiceProvider>(MockBehavior.Strict);
            serviceProviderMock.Setup(p => p.GetService(typeof(IScriptHostManager))).Returns(scriptHostManagerMock.Object);

            _functionsHostingConfigOptions = Options.Create(new FunctionsHostingConfigOptions());

            _rpcWorkerProcess = new RpcWorkerProcess("node",
                "testworkerId",
                "testrootPath",
                rpcServer.Uri,
                testWorkerConfigs.ElementAt(0),
                _eventManager.Object,
                _workerProcessFactory.Object,
                processRegistry.Object,
                _logger,
                languageWorkerConsoleLogSource.Object,
                new TestMetricsLogger(),
                serviceProviderMock.Object,
                _functionsHostingConfigOptions,
                testEnv,
                new LoggerFactory());
        }

        [Fact]
        public void Constructor_RegistersHostStartedEvent()
        {
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
            Process process = Process.GetCurrentProcess();
            _rpcWorkerProcess.Process = process;

            _hostProcessMonitorMock.Setup(p => p.RegisterChildProcess(process));

            HostStartEvent evt = new HostStartEvent();
            _rpcWorkerProcess.OnHostStart(evt);
            _hostProcessMonitorMock.Verify(p => p.RegisterChildProcess(process), Times.Once);
        }

        [Fact]
        public void RegisterWithProcessMonitor_Succeeds()
        {
            Process process = Process.GetCurrentProcess();
            _rpcWorkerProcess.Process = process;

            _hostProcessMonitorMock.Setup(p => p.RegisterChildProcess(process));

            _rpcWorkerProcess.RegisterWithProcessMonitor();
            _hostProcessMonitorMock.Verify(p => p.RegisterChildProcess(process), Times.Once);

            // registration is skipped if attempted again for the same monitor
            _rpcWorkerProcess.RegisterWithProcessMonitor();
            _hostProcessMonitorMock.Verify(p => p.RegisterChildProcess(process), Times.Once);

            // if the monitor changes (e.g. due to a host restart)
            // registration is performed
            _hostProcessMonitorMock = new Mock<IHostProcessMonitor>(MockBehavior.Strict);
            _hostProcessMonitorMock.Setup(p => p.RegisterChildProcess(process));
            _rpcWorkerProcess.RegisterWithProcessMonitor();
            _hostProcessMonitorMock.Verify(p => p.RegisterChildProcess(process), Times.Once);
        }

        [Fact]
        public void UnregisterFromProcessMonitor_Succeeds()
        {
            Process process = Process.GetCurrentProcess();
            _rpcWorkerProcess.Process = process;

            _hostProcessMonitorMock.Setup(p => p.RegisterChildProcess(process));
            _hostProcessMonitorMock.Setup(p => p.UnregisterChildProcess(process));

            // not yet registered so noop
            _rpcWorkerProcess.UnregisterFromProcessMonitor();
            _hostProcessMonitorMock.Verify(p => p.UnregisterChildProcess(process), Times.Never);

            _rpcWorkerProcess.RegisterWithProcessMonitor();
            _rpcWorkerProcess.UnregisterFromProcessMonitor();
            _hostProcessMonitorMock.Verify(p => p.UnregisterChildProcess(process), Times.Once);

            // attempting to unregister again is a noop
            _rpcWorkerProcess.UnregisterFromProcessMonitor();
            _hostProcessMonitorMock.Verify(p => p.UnregisterChildProcess(process), Times.Once);
        }

        [Fact]
        public void ErrorMessageQueue_Empty()
        {
            Assert.Empty(_rpcWorkerProcess.ProcessStdErrDataQueue);
        }

        [Fact]
        public void ErrorMessageQueue_Enqueue_Success()
        {
            WorkerProcessUtilities.AddStdErrMessage(_rpcWorkerProcess.ProcessStdErrDataQueue, "Error1");
            WorkerProcessUtilities.AddStdErrMessage(_rpcWorkerProcess.ProcessStdErrDataQueue, "Error2");

            Assert.True(_rpcWorkerProcess.ProcessStdErrDataQueue.Count == 2);
            string exceptionMessage = string.Join(",", _rpcWorkerProcess.ProcessStdErrDataQueue.Where(s => !string.IsNullOrEmpty(s)));
            Assert.Equal("Error1,Error2", exceptionMessage);
        }

        [Fact]
        public void ErrorMessageQueue_Full_Enqueue_Success()
        {
            WorkerProcessUtilities.AddStdErrMessage(_rpcWorkerProcess.ProcessStdErrDataQueue, "Error1");
            WorkerProcessUtilities.AddStdErrMessage(_rpcWorkerProcess.ProcessStdErrDataQueue, "Error2");
            WorkerProcessUtilities.AddStdErrMessage(_rpcWorkerProcess.ProcessStdErrDataQueue, "Error3");
            WorkerProcessUtilities.AddStdErrMessage(_rpcWorkerProcess.ProcessStdErrDataQueue, "Error4");
            Assert.True(_rpcWorkerProcess.ProcessStdErrDataQueue.Count == 3);
            string exceptionMessage = string.Join(",", _rpcWorkerProcess.ProcessStdErrDataQueue.Where(s => !string.IsNullOrEmpty(s)));
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
            _rpcWorkerProcess.HandleWorkerProcessRestart();

            _eventManager.Verify(_ => _.Publish(It.IsAny<WorkerRestartEvent>()), Times.Once());
            _eventManager.Verify(_ => _.Publish(It.IsAny<WorkerErrorEvent>()), Times.Never());
        }

        [Fact]
        public void WorkerProcess_Dispose()
        {
            Process process = new Process();
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = $"ls";
            process.StartInfo = startInfo;
            process.Start();

            _rpcWorkerProcess.Process = process;
            _rpcWorkerProcess.Dispose();
            var traces = _logger.GetLogMessages();
            var disposeLogs = traces.Where(m => string.Equals(m.FormattedMessage, "Worker process has not exited despite waiting for 1000 ms"));
            Assert.False(disposeLogs.Any());
        }

        [Fact]
        public void CreateWorkerProcess_AddsHostingConfiguration()
        {
            _functionsHostingConfigOptions.Value.Features["feature1"] = "1";
            var process = _rpcWorkerProcess.CreateWorkerProcess();

            _workerProcessFactory.Verify(x => x.CreateWorkerProcess(It.Is<WorkerContext>(c => c.EnvironmentVariables["feature1"] == "1")));
        }

        [Fact]
        public void WorkerProcess_WaitForExit_AfterExit_DoesNotThrow()
        {
            Process process = new Process();
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = $"ls";
            process.StartInfo = startInfo;
            process.Start();

            _rpcWorkerProcess.Process = process;
            _rpcWorkerProcess.Dispose();

            _rpcWorkerProcess.WaitForProcessExitInMilliSeconds(1);

            var traces = _logger.GetLogMessages();
            string msg = traces.Single().FormattedMessage;
            Assert.StartsWith("An exception was thrown while waiting for a worker process to exit.", msg);

            Exception ex = traces.Single().Exception;
            Assert.IsType<InvalidOperationException>(ex);
        }
    }
}