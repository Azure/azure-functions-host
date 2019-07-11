// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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

        public LanguageWorkerProcessTests()
        {
            var eventManager = new Mock<IScriptEventManager>();
            var workerProcessFactory = new Mock<IWorkerProcessFactory>();
            var processRegistry = new Mock<IProcessRegistry>();
            var rpcServer = new TestRpcServer();
            var scriptJobHostEnvironment = new Mock<IScriptJobHostEnvironment>();
            var testEnv = new TestEnvironment();
            _languageWorkerProcess = new LanguageWorkerProcess("node",
                "testworkerId",
                "testrootPath",
                rpcServer.Uri,
                null,
                eventManager.Object,
                workerProcessFactory.Object,
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
    }
}