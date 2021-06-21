// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers
{
    public class WorkerConsoleLogServiceTests
    {
        private IScriptEventManager _eventManager;
        private IProcessRegistry _processRegistry;
        private TestLogger _testLogger = new TestLogger("test");
        private WorkerConsoleLogService _workerConsoleLogService;
        private WorkerConsoleLogSource _workerConsoleLogSource;
        private Mock<IServiceProvider> _serviceProviderMock;

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task WorkerConsoleLogService_ConsoleLogs_LogLevel_Expected(bool useStdErrForErroLogsOnly)
        {
            _workerConsoleLogSource = new WorkerConsoleLogSource();
            _eventManager = new ScriptEventManager();
            _processRegistry = new EmptyProcessRegistry();
            _workerConsoleLogService = new WorkerConsoleLogService(_testLogger, _workerConsoleLogSource);
            _serviceProviderMock = new Mock<IServiceProvider>(MockBehavior.Strict);
            WorkerProcess workerProcess = new TestWorkerProcess(_eventManager, _processRegistry, _testLogger, _workerConsoleLogSource, null, _serviceProviderMock.Object, useStdErrForErroLogsOnly);
            workerProcess.ParseErrorMessageAndLog("Test Message No keyword");
            workerProcess.ParseErrorMessageAndLog("Test Error Message");
            workerProcess.ParseErrorMessageAndLog("Test Warning Message");

            workerProcess.BuildAndLogConsoleLog("LanguageWorkerConsoleLog[Test worker log]", LogLevel.Information);

            _ = _workerConsoleLogService.ProcessLogs().ContinueWith(t => { });
            await _workerConsoleLogService.StopAsync(System.Threading.CancellationToken.None);
            var allLogs = _testLogger.GetLogMessages();
            Assert.True(allLogs.Count == 4);
            VerifyLogLevel(allLogs, "Test Error Message", LogLevel.Error);
            VerifyLogLevel(allLogs, "Test Warning Message", LogLevel.Warning);
            if (useStdErrForErroLogsOnly)
            {
                VerifyLogLevel(allLogs, "Test Message No keyword", LogLevel.Error);
            }
            else
            {
                VerifyLogLevel(allLogs, "Test Message No keyword", LogLevel.Information);
            }

            VerifyLogLevel(allLogs, "[Test worker log]", LogLevel.Debug);
        }

        private static void VerifyLogLevel(IList<LogMessage> allLogs, string msg, LogLevel expectedLevel)
        {
            var message = allLogs.Where(l => l.FormattedMessage.Contains(msg)).FirstOrDefault();
            Assert.NotNull(message);
            Assert.DoesNotContain(WorkerConstants.LanguageWorkerConsoleLogPrefix, message.FormattedMessage);
            Assert.Equal(expectedLevel, message.Level);
        }
    }
}
