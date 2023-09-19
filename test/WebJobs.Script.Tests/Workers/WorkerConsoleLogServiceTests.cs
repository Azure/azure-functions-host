// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Castle.Core.Logging;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers
{
    public class WorkerConsoleLogServiceTests
    {
        private IScriptEventManager _eventManager;
        private IProcessRegistry _processRegistry;
        private TestLogger _testUserLogger = new TestLogger("Host.Function.Console");
        private TestLogger _testSystemLogger = new TestLogger("Worker.rpcWorkerProcess");
        private WorkerConsoleLogService _workerConsoleLogService;
        private WorkerConsoleLogSource _workerConsoleLogSource;
        private Mock<IServiceProvider> _serviceProviderMock;
        private static Microsoft.Extensions.Logging.ILogger _testLogger;
        private static TestLoggerProvider _testLoggerProvider;
        private static LoggerFactory _testLoggerFactory;

        public WorkerConsoleLogServiceTests()
        {
            _testLoggerProvider = new TestLoggerProvider();
            _testLoggerFactory = new LoggerFactory();
            _testLoggerFactory.AddProvider(_testLoggerProvider);
            _testLogger = _testLoggerProvider.CreateLogger(WorkerConstants.ToolingConsoleLogCategoryName);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task WorkerConsoleLogService_ConsoleLogs_LogLevel_Expected(bool useStdErrForErrorLogsOnly)
        {
            // Arrange
            _workerConsoleLogSource = new WorkerConsoleLogSource();
            _eventManager = new ScriptEventManager();
            _processRegistry = new EmptyProcessRegistry();
            _workerConsoleLogService = new WorkerConsoleLogService(_testUserLogger, _workerConsoleLogSource);
            _serviceProviderMock = new Mock<IServiceProvider>(MockBehavior.Strict);

            WorkerProcess workerProcess = new TestWorkerProcess(_eventManager, _processRegistry, _testSystemLogger, _workerConsoleLogSource, null, _serviceProviderMock.Object, _testLoggerFactory, Mock.Of<IEnvironment>(), useStdErrForErrorLogsOnly);
            workerProcess.ParseErrorMessageAndLog("Test Message No keyword");
            workerProcess.ParseErrorMessageAndLog("Test Error Message");
            workerProcess.ParseErrorMessageAndLog("Test Warning Message");
            workerProcess.ParseErrorMessageAndLog("LanguageWorkerConsoleLog[Test Worker Message No keyword]");
            workerProcess.ParseErrorMessageAndLog("LanguageWorkerConsoleLog[Test Worker Error Message]");
            workerProcess.ParseErrorMessageAndLog("LanguageWorkerConsoleLog[Test Worker Warning Message]");
            workerProcess.ParseErrorMessageAndLog("azfuncjsonlog:{ 'name':'dotnet-worker-startup', 'workerProcessId' : 321 }");

            // Act
            _ = _workerConsoleLogService.ProcessLogs().ContinueWith(t => { });
            await _workerConsoleLogService.StopAsync(System.Threading.CancellationToken.None);
            var userLogs = _testUserLogger.GetLogMessages();
            var systemLogs = _testSystemLogger.GetLogMessages();
            var toolingConsoleLogs = _testLoggerProvider.GetAllLogMessages();

            // Assert
            Assert.Equal(3, userLogs.Count);
            Assert.Equal(3, systemLogs.Count);
            Assert.Equal(1, toolingConsoleLogs.Count);

            VerifyLogLevel(userLogs, "Test Error Message", LogLevel.Error);
            VerifyLogLevel(systemLogs, "[Test Worker Error Message]", LogLevel.Error);
            VerifyLogLevel(userLogs, "Test Warning Message", LogLevel.Warning);
            VerifyLogLevel(systemLogs, "[Test Worker Warning Message]", LogLevel.Warning);

            if (useStdErrForErrorLogsOnly)
            {
                VerifyLogLevel(userLogs, "Test Message No keyword", LogLevel.Error);
                VerifyLogLevel(systemLogs, "[Test Worker Message No keyword]", LogLevel.Error);
                VerifyLogLevel(toolingConsoleLogs, "azfuncjsonlog:{ 'name':'dotnet-worker-startup', 'workerProcessId' : 321 }", LogLevel.Error);
            }
            else
            {
                VerifyLogLevel(userLogs, "Test Message No keyword", LogLevel.Information);
                VerifyLogLevel(systemLogs, "[Test Worker Message No keyword]", LogLevel.Information);
                VerifyLogLevel(toolingConsoleLogs, "azfuncjsonlog:{ 'name':'dotnet-worker-startup', 'workerProcessId' : 321 }", LogLevel.Information);
            }

            Assert.True(toolingConsoleLogs.All(l => l.FormattedMessage.StartsWith(WorkerConstants.ToolingConsoleLogPrefix)));
        }

        private static void VerifyLogLevel(IList<LogMessage> allLogs, string msg, LogLevel expectedLevel)
        {
            var message = allLogs.FirstOrDefault(l => l.FormattedMessage.Contains(msg));
            Assert.NotNull(message);
            Assert.DoesNotContain(WorkerConstants.LanguageWorkerConsoleLogPrefix, message.FormattedMessage);
            Assert.Equal(expectedLevel, message.Level);
        }
    }
}
