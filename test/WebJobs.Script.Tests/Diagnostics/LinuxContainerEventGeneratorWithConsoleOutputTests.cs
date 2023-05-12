// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class LinuxContainerEventGeneratorWithConsoleOutputTests : IDisposable
    {
        private readonly MemoryStream _consoleOut = new MemoryStream();
        private readonly string _containerName = "test-container";
        private readonly string _stampName = "test-stamp";
        private readonly string _tenantId = "test-tenant";
        private readonly string _testNodeAddress = "test-address";

        public LinuxContainerEventGeneratorWithConsoleOutputTests()
        {
            var streamWriter = new StreamWriter(_consoleOut);
            streamWriter.AutoFlush = true;
            Console.SetOut(streamWriter);
        }

        public void Dispose()
        {
            var standardOutput = new StreamWriter(Console.OpenStandardOutput());
            standardOutput.AutoFlush = true;
            Console.SetOut(standardOutput);
        }

        private IEnvironment CreateEnvironment(bool consoleDisabled = false, int? bufferSize = null, bool? batched = null)
        {
            var mockEnvironment = new Mock<IEnvironment>(MockBehavior.Strict);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.ContainerName)).Returns(_containerName);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteHomeStampName)).Returns(_stampName);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteStampDeploymentId)).Returns(_tenantId);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.LinuxNodeIpAddress)).Returns(_testNodeAddress);

            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.ConsoleLoggingDisabled)).Returns(consoleDisabled ? "1" : "0");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.ConsoleLoggingBufferSize)).Returns(bufferSize?.ToString());
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.ConsoleLoggingBufferBatched)).Returns(
                batched switch
                {
                    null => null,
                    true => "1",
                    false => "0",
                });
            return mockEnvironment.Object;
        }

        [Fact]
        public void GenerateNothingWhenDisabled()
        {
            var env = CreateEnvironment(consoleDisabled: true);
            var generator = new LinuxContainerEventGenerator(env);

            generator.LogFunctionTraceEvent(LogLevel.Information, "C37E3412-86D1-4B93-BC5A-A2AE09D26C2D", "TestApp", "TestFunction", "TestEvent", "TestSource", "These are the details, lots of details", "This is the summary, a great summary", "TestExceptionType", "Test exception message, with details", "E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3", "3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829", "F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53", "TestRuntimeSiteName", "TestSlotName", DateTime.Now);

            using var sr = new StreamReader(_consoleOut);
            _consoleOut.Position = 0;
            var output = sr.ReadToEnd().Trim();

            Assert.Equal(string.Empty, output);
        }

        [Fact]
        public void SingleEventNoBuffer()
        {
            var env = CreateEnvironment(bufferSize: 0);
            var generator = new LinuxContainerEventGenerator(env);

            var timestamp = DateTime.Parse("2023-04-19T14:12:00.0000000Z");
            generator.LogFunctionTraceEvent(LogLevel.Information, "C37E3412-86D1-4B93-BC5A-A2AE09D26C2D", "TestApp", "TestFunction", "TestEvent", "TestSource", "These are the details, lots of details", "This is the summary, a great summary", "TestExceptionType", "Test exception message, with details", "E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3", "3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829", "F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53", "TestRuntimeSiteName", "TestSlotName", timestamp);

            using var sr = new StreamReader(_consoleOut);
            _consoleOut.Position = 0;
            var output = sr.ReadToEnd().Trim();

            Assert.Equal($"MS_FUNCTION_LOGS 4,C37E3412-86D1-4B93-BC5A-A2AE09D26C2D,TestApp,TestFunction,TestEvent,TestSource,\"These are the details, lots of details\",\"This is the summary, a great summary\",{ScriptHost.Version},{timestamp.ToString("O")},TestExceptionType,\"Test exception message, with details\",E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3,3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829,F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53,TEST-CONTAINER,test-stamp,test-tenant,TestRuntimeSiteName,TestSlotName", output);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task SingleEventBuffer(bool batched)
        {
            var env = CreateEnvironment(bufferSize: 10, batched: batched);
            var generator = new LinuxContainerEventGenerator(env);

            var timestamp = DateTime.Parse("2023-04-19T14:12:00.0000000Z");
            generator.LogFunctionTraceEvent(LogLevel.Information, "C37E3412-86D1-4B93-BC5A-A2AE09D26C2D", "TestApp", "TestFunction", "TestEvent", "TestSource", "These are the details, lots of details", "This is the summary, a great summary", "TestExceptionType", "Test exception message, with details", "E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3", "3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829", "F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53", "TestRuntimeSiteName", "TestSlotName", timestamp);
            await Task.Delay(TimeSpan.FromMilliseconds(10));

            using var sr = new StreamReader(_consoleOut);
            _consoleOut.Position = 0;
            var output = sr.ReadToEnd().Trim();

            Assert.Equal($"MS_FUNCTION_LOGS 4,C37E3412-86D1-4B93-BC5A-A2AE09D26C2D,TestApp,TestFunction,TestEvent,TestSource,\"These are the details, lots of details\",\"This is the summary, a great summary\",{ScriptHost.Version},{timestamp.ToString("O")},TestExceptionType,\"Test exception message, with details\",E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3,3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829,F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53,TEST-CONTAINER,test-stamp,test-tenant,TestRuntimeSiteName,TestSlotName", output);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task MultipleEventsBuffered(bool batched)
        {
            var env = CreateEnvironment(bufferSize: 10, batched: batched);
            var generator = new LinuxContainerEventGenerator(env);

            var timestamp = DateTime.Parse("2023-04-19T14:12:00.0000000Z");
            generator.LogFunctionTraceEvent(LogLevel.Information, "C37E3412-86D1-4B93-BC5A-A2AE09D26C2D", "TestApp", "TestFunction1", "TestEvent", "TestSource", "These are the details, lots of details", "This is the summary, a great summary", "TestExceptionType", "Test exception message, with details", "E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3", "3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829", "F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53", "TestRuntimeSiteName", "TestSlotName", timestamp);
            generator.LogFunctionTraceEvent(LogLevel.Information, "C37E3412-86D1-4B93-BC5A-A2AE09D26C2D", "TestApp", "TestFunction2", "TestEvent", "TestSource", "These are the details, lots of details", "This is the summary, a great summary", "TestExceptionType", "Test exception message, with details", "E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3", "3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829", "F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53", "TestRuntimeSiteName", "TestSlotName", timestamp);
            generator.LogFunctionTraceEvent(LogLevel.Information, "C37E3412-86D1-4B93-BC5A-A2AE09D26C2D", "TestApp", "TestFunction3", "TestEvent", "TestSource", "These are the details, lots of details", "This is the summary, a great summary", "TestExceptionType", "Test exception message, with details", "E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3", "3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829", "F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53", "TestRuntimeSiteName", "TestSlotName", timestamp);
            await Task.Delay(TimeSpan.FromMilliseconds(10));

            using var sr = new StreamReader(_consoleOut);
            _consoleOut.Position = 0;
            var output = sr.ReadToEnd().Trim().SplitLines();

            Assert.Equal(3, output.Length);

            Assert.Equal($"MS_FUNCTION_LOGS 4,C37E3412-86D1-4B93-BC5A-A2AE09D26C2D,TestApp,TestFunction1,TestEvent,TestSource,\"These are the details, lots of details\",\"This is the summary, a great summary\",{ScriptHost.Version},{timestamp.ToString("O")},TestExceptionType,\"Test exception message, with details\",E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3,3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829,F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53,TEST-CONTAINER,test-stamp,test-tenant,TestRuntimeSiteName,TestSlotName", output[0]);
            Assert.Equal($"MS_FUNCTION_LOGS 4,C37E3412-86D1-4B93-BC5A-A2AE09D26C2D,TestApp,TestFunction2,TestEvent,TestSource,\"These are the details, lots of details\",\"This is the summary, a great summary\",{ScriptHost.Version},{timestamp.ToString("O")},TestExceptionType,\"Test exception message, with details\",E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3,3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829,F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53,TEST-CONTAINER,test-stamp,test-tenant,TestRuntimeSiteName,TestSlotName", output[1]);
            Assert.Equal($"MS_FUNCTION_LOGS 4,C37E3412-86D1-4B93-BC5A-A2AE09D26C2D,TestApp,TestFunction3,TestEvent,TestSource,\"These are the details, lots of details\",\"This is the summary, a great summary\",{ScriptHost.Version},{timestamp.ToString("O")},TestExceptionType,\"Test exception message, with details\",E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3,3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829,F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53,TEST-CONTAINER,test-stamp,test-tenant,TestRuntimeSiteName,TestSlotName", output[2]);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MultipleEventsWithTinyBuffer_WritesDirectlyToConsoleOnTimeout(bool batched)
        {
            // setup in a state where the buffer isn't being processed and can only hold two messages
            var env = CreateEnvironment(bufferSize: 2, batched: batched);
            var consoleWriter = new ConsoleWriter(env, LinuxContainerEventGenerator.LogUnhandledException, consoleBufferTimeout: TimeSpan.FromMilliseconds(10), autoStart: false);
            var generator = new LinuxContainerEventGenerator(env, consoleWriter);

            var timestamp = DateTime.Parse("2023-04-19T14:12:00.0000000Z");
            generator.LogFunctionTraceEvent(LogLevel.Information, "C37E3412-86D1-4B93-BC5A-A2AE09D26C2D", "TestApp", "TestFunction1", "TestEvent", "TestSource", "These are the details, lots of details", "This is the summary, a great summary", "TestExceptionType", "Test exception message, with details", "E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3", "3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829", "F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53", "TestRuntimeSiteName", "TestSlotName", timestamp);
            generator.LogFunctionTraceEvent(LogLevel.Information, "C37E3412-86D1-4B93-BC5A-A2AE09D26C2D", "TestApp", "TestFunction2", "TestEvent", "TestSource", "These are the details, lots of details", "This is the summary, a great summary", "TestExceptionType", "Test exception message, with details", "E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3", "3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829", "F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53", "TestRuntimeSiteName", "TestSlotName", timestamp);

            // This write will block until the above timeout expires, and then it should write directly to the console
            generator.LogFunctionTraceEvent(LogLevel.Information, "C37E3412-86D1-4B93-BC5A-A2AE09D26C2D", "TestApp", "TestFunction3", "TestEvent", "TestSource", "These are the details, lots of details", "This is the summary, a great summary", "TestExceptionType", "Test exception message, with details", "E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3", "3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829", "F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53", "TestRuntimeSiteName", "TestSlotName", timestamp);

            using var sr = new StreamReader(_consoleOut);
            _consoleOut.Position = 0;
            var output = sr.ReadToEnd().Trim().SplitLines();

            // The first two messages are still stuck in the buffer. The third message will have been written to the console.
            // We should also have a log for the timeout exception that occurred while waiting for the buffer to become available.
            Assert.Equal(2, output.Length);
            Assert.StartsWith("MS_FUNCTION_LOGS 2,,,,,LogUnhandledException,\"System.OperationCanceledException: The operation was canceled.", output[0]);
            Assert.Equal($"MS_FUNCTION_LOGS 4,C37E3412-86D1-4B93-BC5A-A2AE09D26C2D,TestApp,TestFunction3,TestEvent,TestSource,\"These are the details, lots of details\",\"This is the summary, a great summary\",{ScriptHost.Version},{timestamp.ToString("O")},TestExceptionType,\"Test exception message, with details\",E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3,3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829,F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53,TEST-CONTAINER,test-stamp,test-tenant,TestRuntimeSiteName,TestSlotName", output[1]);
        }

        [Fact]
        public async Task MultipleEventsBatchedWithTinyBuffer_BlocksOnWriteOnBufferFull_IfTimeoutNotReached()
        {
            // setup in a state where the buffer isn't being processed and can only hold two messages
            var env = CreateEnvironment(bufferSize: 2, batched: true);
            var consoleWriter = new ConsoleWriter(env, LinuxContainerEventGenerator.LogUnhandledException, consoleBufferTimeout: TimeSpan.FromMilliseconds(500), autoStart: false);
            var generator = new LinuxContainerEventGenerator(env, consoleWriter);

            var timestamp = DateTime.Parse("2023-04-19T14:12:00.0000000Z");
            generator.LogFunctionTraceEvent(LogLevel.Information, "C37E3412-86D1-4B93-BC5A-A2AE09D26C2D", "TestApp", "TestFunction1", "TestEvent", "TestSource", "These are the details, lots of details", "This is the summary, a great summary", "TestExceptionType", "Test exception message, with details", "E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3", "3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829", "F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53", "TestRuntimeSiteName", "TestSlotName", timestamp);
            generator.LogFunctionTraceEvent(LogLevel.Information, "C37E3412-86D1-4B93-BC5A-A2AE09D26C2D", "TestApp", "TestFunction2", "TestEvent", "TestSource", "These are the details, lots of details", "This is the summary, a great summary", "TestExceptionType", "Test exception message, with details", "E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3", "3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829", "F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53", "TestRuntimeSiteName", "TestSlotName", timestamp);

            // The third write will block until the first two are flushed.
            var logTask = Task.Run(() => generator.LogFunctionTraceEvent(LogLevel.Information, "C37E3412-86D1-4B93-BC5A-A2AE09D26C2D", "TestApp", "TestFunction3", "TestEvent", "TestSource", "These are the details, lots of details", "This is the summary, a great summary", "TestExceptionType", "Test exception message, with details", "E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3", "3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829", "F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53", "TestRuntimeSiteName", "TestSlotName", timestamp));
            await Task.Delay(TimeSpan.FromMilliseconds(10));
            Assert.False(logTask.IsCompleted);

            consoleWriter.StartProcessingBuffer(batched: true);
            await Task.Delay(TimeSpan.FromMilliseconds(50));
            Assert.True(logTask.IsCompleted);

            using var sr = new StreamReader(_consoleOut);
            _consoleOut.Position = 0;
            var output = sr.ReadToEnd().Trim().SplitLines();

            Assert.Equal(3, output.Length);

            Assert.Equal($"MS_FUNCTION_LOGS 4,C37E3412-86D1-4B93-BC5A-A2AE09D26C2D,TestApp,TestFunction1,TestEvent,TestSource,\"These are the details, lots of details\",\"This is the summary, a great summary\",{ScriptHost.Version},{timestamp.ToString("O")},TestExceptionType,\"Test exception message, with details\",E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3,3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829,F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53,TEST-CONTAINER,test-stamp,test-tenant,TestRuntimeSiteName,TestSlotName", output[0]);
            Assert.Equal($"MS_FUNCTION_LOGS 4,C37E3412-86D1-4B93-BC5A-A2AE09D26C2D,TestApp,TestFunction2,TestEvent,TestSource,\"These are the details, lots of details\",\"This is the summary, a great summary\",{ScriptHost.Version},{timestamp.ToString("O")},TestExceptionType,\"Test exception message, with details\",E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3,3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829,F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53,TEST-CONTAINER,test-stamp,test-tenant,TestRuntimeSiteName,TestSlotName", output[1]);
            Assert.Equal($"MS_FUNCTION_LOGS 4,C37E3412-86D1-4B93-BC5A-A2AE09D26C2D,TestApp,TestFunction3,TestEvent,TestSource,\"These are the details, lots of details\",\"This is the summary, a great summary\",{ScriptHost.Version},{timestamp.ToString("O")},TestExceptionType,\"Test exception message, with details\",E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3,3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829,F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53,TEST-CONTAINER,test-stamp,test-tenant,TestRuntimeSiteName,TestSlotName", output[2]);
        }

        [Fact]
        public async Task FlushesBufferOnDispose()
        {
            var env = CreateEnvironment(bufferSize: 10, batched: true);
            var consoleWriter = new ConsoleWriter(env, LinuxContainerEventGenerator.LogUnhandledException, consoleBufferTimeout: TimeSpan.FromMilliseconds(500), autoStart: true);
            var generator = new LinuxContainerEventGenerator(env, consoleWriter);

            // Setup console output that will block until we release the semaphore.
            var controlledWriter = new ControlledWriter(_consoleOut);
            await controlledWriter.Semaphore.WaitAsync();
            Console.SetOut(controlledWriter);

            var timestamp = DateTime.Parse("2023-04-19T14:12:00.0000000Z");
            generator.LogFunctionTraceEvent(LogLevel.Information, "C37E3412-86D1-4B93-BC5A-A2AE09D26C2D", "TestApp", "TestFunction1", "TestEvent", "TestSource", "These are the details, lots of details", "This is the summary, a great summary", "TestExceptionType", "Test exception message, with details", "E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3", "3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829", "F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53", "TestRuntimeSiteName", "TestSlotName", timestamp);
            generator.LogFunctionTraceEvent(LogLevel.Information, "C37E3412-86D1-4B93-BC5A-A2AE09D26C2D", "TestApp", "TestFunction2", "TestEvent", "TestSource", "These are the details, lots of details", "This is the summary, a great summary", "TestExceptionType", "Test exception message, with details", "E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3", "3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829", "F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53", "TestRuntimeSiteName", "TestSlotName", timestamp);
            await Task.Delay(TimeSpan.FromMilliseconds(20));

            // We haven't released the semaphore yet, so nothing should have been written.
            Assert.Equal(0, _consoleOut.Length);

            var disposeTask = Task.Run(consoleWriter.Dispose);
            await Task.Delay(TimeSpan.FromMilliseconds(20));

            // Dispose should be blocked on flushing the buffer, which is blocked on the semaphore.
            Assert.False(disposeTask.IsCompleted);

            controlledWriter.Semaphore.Release();
            await disposeTask;

            // after being disposed, console writer should fall back to direct console write, just so that we don't get errors on logging paths
            generator.LogFunctionTraceEvent(LogLevel.Information, "C37E3412-86D1-4B93-BC5A-A2AE09D26C2D", "TestApp", "TestFunction3", "TestEvent", "TestSource", "These are the details, lots of details", "This is the summary, a great summary", "TestExceptionType", "Test exception message, with details", "E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3", "3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829", "F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53", "TestRuntimeSiteName", "TestSlotName", timestamp);

            using var sr = new StreamReader(_consoleOut);
            _consoleOut.Position = 0;
            var output = sr.ReadToEnd().Trim().SplitLines();

            Assert.Equal(3, output.Length);

            Assert.Equal($"MS_FUNCTION_LOGS 4,C37E3412-86D1-4B93-BC5A-A2AE09D26C2D,TestApp,TestFunction1,TestEvent,TestSource,\"These are the details, lots of details\",\"This is the summary, a great summary\",{ScriptHost.Version},{timestamp.ToString("O")},TestExceptionType,\"Test exception message, with details\",E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3,3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829,F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53,TEST-CONTAINER,test-stamp,test-tenant,TestRuntimeSiteName,TestSlotName", output[0]);
            Assert.Equal($"MS_FUNCTION_LOGS 4,C37E3412-86D1-4B93-BC5A-A2AE09D26C2D,TestApp,TestFunction2,TestEvent,TestSource,\"These are the details, lots of details\",\"This is the summary, a great summary\",{ScriptHost.Version},{timestamp.ToString("O")},TestExceptionType,\"Test exception message, with details\",E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3,3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829,F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53,TEST-CONTAINER,test-stamp,test-tenant,TestRuntimeSiteName,TestSlotName", output[1]);
            Assert.Equal($"MS_FUNCTION_LOGS 4,C37E3412-86D1-4B93-BC5A-A2AE09D26C2D,TestApp,TestFunction3,TestEvent,TestSource,\"These are the details, lots of details\",\"This is the summary, a great summary\",{ScriptHost.Version},{timestamp.ToString("O")},TestExceptionType,\"Test exception message, with details\",E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3,3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829,F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53,TEST-CONTAINER,test-stamp,test-tenant,TestRuntimeSiteName,TestSlotName", output[2]);
        }

        private class ControlledWriter : StreamWriter
        {
            public ControlledWriter(Stream stream) : base(stream)
            {
                this.AutoFlush = true;
            }

            public SemaphoreSlim Semaphore { get; set; } = new SemaphoreSlim(1);

            public override void Write(string value)
            {
                Semaphore.Wait();
                base.Write(value);
                Semaphore.Release();
            }

            public override void WriteLine(string value)
            {
                Semaphore.Wait();
                base.WriteLine(value);
                Semaphore.Release();
            }
        }
    }
}
