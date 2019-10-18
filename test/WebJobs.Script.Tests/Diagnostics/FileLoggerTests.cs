// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class FileLoggerTests
    {
        private readonly string _category;
        private readonly string _logFilePath;
        private readonly FileWriter _fileWriter;
        private readonly LoggerExternalScopeProvider _scopeProvider;

        public FileLoggerTests()
        {
            _logFilePath = Path.Combine(Path.GetTempPath(), "WebJobs.Script.Tests", "FileLoggerTests");
            _category = LogCategories.CreateFunctionCategory("Test");

            if (Directory.Exists(_logFilePath))
            {
                Directory.Delete(_logFilePath, recursive: true);
            }

            _fileWriter = new FileWriter(_logFilePath);

            _scopeProvider = new LoggerExternalScopeProvider();
        }

        [Fact]
        public void FileLogger_DoesNotLog_IfNoText()
        {
            ILogger logger = new FileLogger(_category, _fileWriter, isFileLoggingEnabled: () => true, isPrimary: () => true, logType: LogType.Host, _scopeProvider);
            logger.LogInformation("Line 1");
            logger.LogInformation(string.Empty);
            logger.LogInformation(null); // The ILogger extensions replace nulls with "[null]"
            logger.LogInformation("Line 2");
            logger.Log<object>(LogLevel.Information, 0, null, null, (s, e) => null); // Test if we send an actual null.

            _fileWriter.Flush();

            string logFile = Directory.EnumerateFiles(_logFilePath).Single();
            string[] actual = File.ReadAllLines(logFile);

            Assert.Collection(actual,
                t => Assert.EndsWith("Line 1", t),
                t => Assert.EndsWith("[null]", t),
                t => Assert.EndsWith("Line 2", t));
        }

        [Fact]
        public void FileLogger_DoesNotLog_IfSystemLog()
        {
            ILogger logger = new FileLogger(_category, _fileWriter, isFileLoggingEnabled: () => true, isPrimary: () => true, logType: LogType.Host, _scopeProvider);

            // We send this value as state in order to mark a log as a System log.
            var isSystemTrace = new Dictionary<string, object> { [ScriptConstants.LogPropertyIsSystemLogKey] = true };
            logger.Log(LogLevel.Information, 0, isSystemTrace, null, (s, e) => "System trace: true");

            var isNotSystemTrace = new Dictionary<string, object> { [ScriptConstants.LogPropertyIsSystemLogKey] = false };
            logger.Log(LogLevel.Information, 0, isNotSystemTrace, null, (s, e) => "System trace: false");

            logger.Log<object>(LogLevel.Information, 0, null, null, (s, e) => "System trace: none");

            _fileWriter.Flush();

            string logFile = Directory.EnumerateFiles(_logFilePath).Single();
            string[] actual = File.ReadAllLines(logFile);

            Assert.Collection(actual,
                t => Assert.EndsWith("System trace: false", t),
                t => Assert.EndsWith("System trace: none", t));
        }

        [Fact]
        public void FileLogger_DoesNotLogPrimaryLog_IfNotPrimary()
        {
            ILogger logger = new FileLogger(_category, _fileWriter, isFileLoggingEnabled: () => true, isPrimary: () => false, logType: LogType.Host, _scopeProvider);

            // We send this value as state in order to mark a log as a System log.
            var isSystemTrace = new Dictionary<string, object> { [ScriptConstants.LogPropertyPrimaryHostKey] = true };
            logger.Log(LogLevel.Information, 0, isSystemTrace, null, (s, e) => "Primary trace: true");

            var isNotSystemTrace = new Dictionary<string, object> { [ScriptConstants.LogPropertyPrimaryHostKey] = false };
            logger.Log(LogLevel.Information, 0, isNotSystemTrace, null, (s, e) => "Primary trace: false");

            logger.Log<object>(LogLevel.Information, 0, null, null, (s, e) => "Primary trace: none");

            _fileWriter.Flush();

            string logFile = Directory.EnumerateFiles(_logFilePath).Single();
            string[] actual = File.ReadAllLines(logFile);

            Assert.Collection(actual,
                t => Assert.EndsWith("Primary trace: false", t),
                t => Assert.EndsWith("Primary trace: none", t));
        }

        [Fact]
        public void FileLogger_DoesNotLog_IfFileLoggingDisabled()
        {
            bool fileLoggingEnabled = true;

            Func<bool> isFileLoggingEnabled = () =>
            {
                return fileLoggingEnabled;
            };

            ILogger logger = new FileLogger(_category, _fileWriter, isFileLoggingEnabled, isPrimary: () => true, logType: LogType.Host, _scopeProvider);

            // We send this value as state in order to mark a log as a System log.
            logger.LogInformation("1");
            logger.LogInformation("2");

            fileLoggingEnabled = false;
            logger.LogInformation("3");
            logger.LogInformation("4");

            fileLoggingEnabled = true;
            logger.LogInformation("5");
            logger.LogInformation("6");

            _fileWriter.Flush();

            string logFile = Directory.EnumerateFiles(_logFilePath).Single();
            string[] actual = File.ReadAllLines(logFile);

            Assert.Collection(actual,
                t => Assert.EndsWith("1", t),
                t => Assert.EndsWith("2", t),
                t => Assert.EndsWith("5", t),
                t => Assert.EndsWith("6", t));
        }

        [Fact]
        public void FileLogger_LogsExpectedLines()
        {
            var logger = new FileLogger(_category, _fileWriter, () => true, isPrimary: () => true, logType: LogType.Host, _scopeProvider);

            var logData = new Dictionary<string, object>();
            logger.Log(LogLevel.Trace, 0, logData, null, (s, e) => "Test Message");

            logData.Add(ScriptConstants.LogPropertyFunctionNameKey, "TestFunction");
            logger.Log(LogLevel.Information, 0, logData, null, (s, e) => "Test Message With Function");

            _fileWriter.Flush();

            string logFile = Directory.EnumerateFiles(_logFilePath).Single();
            string[] lines = File.ReadAllLines(logFile);

            Assert.Collection(lines,
                t => Assert.EndsWith("[Trace] Test Message", t),
                t => Assert.EndsWith("[Information,TestFunction] Test Message With Function", t));
        }

        [Fact]
        public void FileLogger_LogsFunctionInvocationException()
        {
            var logger = new FileLogger(_category, _fileWriter, () => true, isPrimary: () => true, logType: LogType.Host, _scopeProvider);

            // throw an exception so we have a stack
            try
            {
                var innerEx = new InvalidOperationException("boom!");
                var ex = new FunctionInvocationException("Error executing function.", innerEx);
                throw ex;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Test Message");
            }

            _fileWriter.Flush();

            string logFile = Directory.EnumerateFiles(_logFilePath).Single();
            string[] lines = File.ReadAllLines(logFile);

            // A FunctionInvocationException only logs the message.
            Assert.Collection(lines,
                t => Assert.EndsWith("[Error] Test Message", t),
                t => Assert.Equal("boom!", t));
        }

        [Fact]
        public void FileLogger_LogsException()
        {
            var logger = new FileLogger(_category, _fileWriter, () => true, isPrimary: () => true, logType: LogType.Host, _scopeProvider);

            // throw an exception so we have a stack
            try
            {
                var ex = new InvalidOperationException("boom!");
                throw ex;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Test Message");
            }

            _fileWriter.Flush();

            string logFile = Directory.EnumerateFiles(_logFilePath).Single();
            string[] lines = File.ReadAllLines(logFile);

            // This isn't a FunctionInvocationException, which means we'll see the stack.
            Assert.Collection(lines,
                t => Assert.EndsWith("[Error] Test Message", t),
                t => Assert.Equal("System.InvalidOperationException : boom!", t),
                t => Assert.Contains("at Microsoft.Azure.WebJobs.Script.Tests.Diagnostics.FileLoggerTests.FileLogger_LogsException()", t));
        }

        [Fact]
        public void GetLogPrefix_ReturnsExpectedValue()
        {
            var state = new Dictionary<string, object>
            {
                [ScriptConstants.LogPropertyFunctionNameKey] = "TestFunction"
            };
            var stateEntries = (IEnumerable<KeyValuePair<string, object>>)state;

            var prefix = FileLogger.GetLogPrefix(state, LogLevel.Information, LogType.Host);
            Assert.Equal("Information,TestFunction", prefix);

            prefix = FileLogger.GetLogPrefix(state, LogLevel.Information, LogType.Function);
            Assert.Equal("Information", prefix);

            prefix = FileLogger.GetLogPrefix(state, LogLevel.Information, LogType.Structured);
            Assert.Equal("Information", prefix);
        }
    }
}
