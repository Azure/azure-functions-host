// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers
{
    public class WorkerConsoleLogServiceTests
    {
        private ILoggerFactory _loggerFactory = new LoggerFactory();
        private IScriptEventManager _eventManager;
        private IProcessRegistry _processRegistry;
        private TestLogger _testLogger = new TestLogger("test");
        private WorkerConsoleLogService _workerConsoleLogService;
        private WorkerConsoleLogSource _workerConsoleLogSource;

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task WorkerConsoleLogService_ConsoleLogs_LogLevel_Expected(bool useStdErrForErroLogsOnly)
        {
            _workerConsoleLogSource = new WorkerConsoleLogSource();
            _eventManager = new ScriptEventManager();
            _processRegistry = new EmptyProcessRegistry();
            _workerConsoleLogService = new WorkerConsoleLogService(_testLogger, _workerConsoleLogSource);
            WorkerProcess workerProcess = new TestWorkerProcess(_eventManager, _processRegistry, _testLogger, _workerConsoleLogSource, null, useStdErrForErroLogsOnly);
            workerProcess.ParseConsoleLog("Test Message");
            workerProcess.ParseConsoleLog("Error:Test Error Message");
            workerProcess.ParseConsoleLog("Warning:Test Warning Info stream Message");
            workerProcess.ParseConsoleLog("Warning:Test Warning Message", true);
            workerProcess.ParseConsoleLog("Test Message No keyword", true);
            _ = _workerConsoleLogService.ProcessLogs().ContinueWith(t => { });
            await _workerConsoleLogService.StopAsync(System.Threading.CancellationToken.None);
            var allLogs = _testLogger.GetLogMessages();
            Assert.True(allLogs.Count == 5);
            VerifyLogLevel(allLogs, "Test Message", LogLevel.Information);
            VerifyLogLevel(allLogs, "Error:Test Error Message", LogLevel.Error);
            VerifyLogLevel(allLogs, "Warning:Test Warning Message", LogLevel.Warning);
            VerifyLogLevel(allLogs, "Warning:Test Warning Info stream Message", LogLevel.Information);
            if (useStdErrForErroLogsOnly)
            {
                VerifyLogLevel(allLogs, "Test Message No keyword", LogLevel.Error);
            }
            else
            {
                VerifyLogLevel(allLogs, "Test Message No keyword", LogLevel.Information);
            }
        }

        private static void VerifyLogLevel(IList<LogMessage> allLogs, string msg, LogLevel expectedLevel)
        {
            var message = allLogs.Where(l => l.FormattedMessage.Contains(msg)).FirstOrDefault();
            Assert.NotNull(message);
            Assert.Equal(expectedLevel, message.Level);
        }
    }
}
