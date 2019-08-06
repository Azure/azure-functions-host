// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{
    public class LanguageWorkerProcessTests
    {
        private LanguageWorkerProcess _languageWorkerProcess;

        private Mock<IWorkerProcessFactory> _workerProcessFactory;

        private Mock<IScriptEventManager> _eventManager;

        public LanguageWorkerProcessTests()
        {
            _workerProcessFactory = new Mock<IWorkerProcessFactory>();
            _workerProcessFactory.Setup(_ => _.CreateWorkerProcess(It.IsAny<WorkerContext>()))
                .Returns(new Process { StartInfo = new ProcessStartInfo { FileName = "file name" } });

            _eventManager = new Mock<IScriptEventManager>();
            var processRegistry = new Mock<IProcessRegistry>();
            var rpcServer = new TestRpcServer();
            var scriptJobHostEnvironment = new Mock<IScriptJobHostEnvironment>();
            var testEnv = new TestEnvironment();
            _languageWorkerProcess = new LanguageWorkerProcess("node",
                "testworkerId",
                "testrootPath",
                rpcServer.Uri,
                null,
                _eventManager.Object,
                _workerProcessFactory.Object,
                processRegistry.Object,
                new TestLogger("test"),
                NullLoggerFactory.Instance);
        }

        [Fact]
        public void ErrorMessageQueue_Empty()
        {
            Assert.Empty(_languageWorkerProcess.ProcessStdErrDataQueue);
        }

        [Fact]
        public void ErrorMessageQueue_Enqueue_Success()
        {
            LanguageWorkerChannelUtilities.AddStdErrMessage(_languageWorkerProcess.ProcessStdErrDataQueue, "Error1");
            LanguageWorkerChannelUtilities.AddStdErrMessage(_languageWorkerProcess.ProcessStdErrDataQueue, "Error2");

            Assert.True(_languageWorkerProcess.ProcessStdErrDataQueue.Count == 2);
            string exceptionMessage = string.Join(",", _languageWorkerProcess.ProcessStdErrDataQueue.Where(s => !string.IsNullOrEmpty(s)));
            Assert.Equal("Error1,Error2", exceptionMessage);
        }

        [Fact]
        public void ErrorMessageQueue_Full_Enqueue_Success()
        {
            LanguageWorkerChannelUtilities.AddStdErrMessage(_languageWorkerProcess.ProcessStdErrDataQueue, "Error1");
            LanguageWorkerChannelUtilities.AddStdErrMessage(_languageWorkerProcess.ProcessStdErrDataQueue, "Error2");
            LanguageWorkerChannelUtilities.AddStdErrMessage(_languageWorkerProcess.ProcessStdErrDataQueue, "Error3");
            LanguageWorkerChannelUtilities.AddStdErrMessage(_languageWorkerProcess.ProcessStdErrDataQueue, "Error4");
            Assert.True(_languageWorkerProcess.ProcessStdErrDataQueue.Count == 3);
            string exceptionMessage = string.Join(",", _languageWorkerProcess.ProcessStdErrDataQueue.Where(s => !string.IsNullOrEmpty(s)));
            Assert.Equal("Error2,Error3,Error4", exceptionMessage);
        }

        [Theory]
        [InlineData("languageWorkerConsoleLog Connection established")]
        [InlineData("LANGUAGEWORKERCONSOLELOG Connection established")]
        [InlineData("LanguageWorkerConsoleLog Connection established")]
        public void IsLanguageWorkerConsoleLog_Returns_True_RemovesLogPrefix(string msg)
        {
            Assert.True(LanguageWorkerChannelUtilities.IsLanguageWorkerConsoleLog(msg));
            Assert.Equal(" Connection established", LanguageWorkerChannelUtilities.RemoveLogPrefix(msg));
        }

        [Theory]
        [InlineData("grpc languageWorkerConsoleLog Connection established")]
        [InlineData("My secret languageWorkerConsoleLog")]
        [InlineData("Connection established")]
        public void IsLanguageWorkerConsoleLog_Returns_False(string msg)
        {
            Assert.False(LanguageWorkerChannelUtilities.IsLanguageWorkerConsoleLog(msg));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(1000)]
        [InlineData(-1000)]
        public void HandleWorkerProcessExitError_PublishesWorkerErrorEventWithException(int exitCode)
        {
            var exception = new LanguageWorkerProcessExitException("Test exception") { ExitCode = exitCode };
            _languageWorkerProcess.HandleWorkerProcessExitError(exception);

            _eventManager.Verify(_ => _.Publish(It.Is<ScriptEvent>(scriptEvent => IsWorkerErrorEvent(scriptEvent, exception))));
        }

        [Theory]
        [InlineData(200)]
        public void HandleWorkerProcessExitError_PublishesWorkerErrorEventWithoutException_OnIntentionalExitCode(int exitCode)
        {
            var exception = new LanguageWorkerProcessExitException("Test exception") { ExitCode = exitCode };
            _languageWorkerProcess.HandleWorkerProcessExitError(exception);

            _eventManager.Verify(_ => _.Publish(It.Is<ScriptEvent>(scriptEvent => IsWorkerErrorEvent(scriptEvent, null))));
        }

        [Theory]
        [InlineData(-1)]
        public void HandleWorkerProcessExitError_DoesNotPublishWorkerErrorEvent_OnMinusOneExitCode(int exitCode)
        {
            var exception = new LanguageWorkerProcessExitException("Test exception") { ExitCode = exitCode };
            _languageWorkerProcess.HandleWorkerProcessExitError(exception);

            _eventManager.Verify(_ => _.Publish(It.IsAny<ScriptEvent>()), Times.Never);
        }

        private static bool IsWorkerErrorEvent(ScriptEvent actualScriptEvent, LanguageWorkerProcessExitException expectedException)
        {
            if (actualScriptEvent is WorkerErrorEvent workerErrorEvent)
            {
                return ReferenceEquals(workerErrorEvent.Exception, expectedException);
            }

            return false;
        }
    }
}