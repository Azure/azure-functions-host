// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Executors
{
    public class FunctionExecutorTests
    {
        private readonly FunctionDescriptor _descriptor;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly Mock<IFunctionInstance> _mockFunctionInstance;
        private readonly TestTraceWriter _traceWriter;
        private readonly TimeSpan _functionTimeout = TimeSpan.FromMinutes(3);

        public FunctionExecutorTests()
        {
            _descriptor = new FunctionDescriptor();
            _mockFunctionInstance = new Mock<IFunctionInstance>(MockBehavior.Strict);
            _mockFunctionInstance.Setup(p => p.FunctionDescriptor).Returns(_descriptor);

            _cancellationTokenSource = new CancellationTokenSource();
            _traceWriter = new TestTraceWriter(TraceLevel.Verbose);
        }

        [Fact]
        public void StartFunctionTimeout_MethodLevelTimeout_CreatesExpectedTimer()
        {
            MethodInfo method = typeof(Functions).GetMethod("MethodLevel", BindingFlags.Static | BindingFlags.Public);
            _descriptor.Method = method;

            // we need to set up the Id so that when the timer fires it doesn't throw, but since this is Strict, we need to access it first.
            _mockFunctionInstance.SetupGet(p => p.Id).Returns(Guid.Empty);
            Assert.NotNull(_mockFunctionInstance.Object.Id);

            TimeoutAttribute attribute = method.GetCustomAttribute<TimeoutAttribute>();

            System.Timers.Timer timer = FunctionExecutor.StartFunctionTimeout(_mockFunctionInstance.Object, attribute, _cancellationTokenSource, _traceWriter, null);

            Assert.True(timer.Enabled);
            Assert.Equal(attribute.Timeout.TotalMilliseconds, timer.Interval);

            _mockFunctionInstance.VerifyAll();
        }

        [Fact]
        public void StartFunctionTimeout_ClassLevelTimeout_CreatesExpectedTimer()
        {
            MethodInfo method = typeof(Functions).GetMethod("ClassLevel", BindingFlags.Static | BindingFlags.Public);
            _descriptor.Method = method;

            // we need to set up the Id so that when the timer fires it doesn't throw, but since this is Strict, we need to access it first.
            _mockFunctionInstance.SetupGet(p => p.Id).Returns(Guid.Empty);
            Assert.NotNull(_mockFunctionInstance.Object.Id);

            TimeoutAttribute attribute = typeof(Functions).GetCustomAttribute<TimeoutAttribute>();

            System.Timers.Timer timer = FunctionExecutor.StartFunctionTimeout(_mockFunctionInstance.Object, attribute, _cancellationTokenSource, _traceWriter, null);

            Assert.True(timer.Enabled);
            Assert.Equal(attribute.Timeout.TotalMilliseconds, timer.Interval);

            _mockFunctionInstance.VerifyAll();
        }

        [Fact]
        public void StartFunctionTimeout_NoTimeout_ReturnsNull()
        {
            TimeoutAttribute timeoutAttribute = null;
            System.Timers.Timer timer = FunctionExecutor.StartFunctionTimeout(null, timeoutAttribute, _cancellationTokenSource, _traceWriter, null);

            Assert.Null(timer);
        }

        [Fact]
        public void StartFunctionTimeout_NoCancellationTokenParameter_ThrowOnTimeoutFalse_ReturnsNull()
        {
            MethodInfo method = typeof(Functions).GetMethod("NoCancellationTokenParameter", BindingFlags.Static | BindingFlags.Public);
            _descriptor.Method = method;

            TimeoutAttribute attribute = typeof(Functions).GetCustomAttribute<TimeoutAttribute>();
            attribute.ThrowOnTimeout = false;

            System.Timers.Timer timer = FunctionExecutor.StartFunctionTimeout(_mockFunctionInstance.Object, attribute, _cancellationTokenSource, _traceWriter, null);

            Assert.Null(timer);

            _mockFunctionInstance.VerifyAll();
        }

        [Fact]
        public void StartFunctionTimeout_NoCancellationTokenParameter_ThrowOnTimeoutTrue_CreatesExpectedTimer()
        {
            MethodInfo method = typeof(Functions).GetMethod("NoCancellationTokenParameter", BindingFlags.Static | BindingFlags.Public);
            _descriptor.Method = method;

            // we need to set up the Id so that when the timer fires it doesn't throw, but since this is Strict, we need to access it first.
            _mockFunctionInstance.SetupGet(p => p.Id).Returns(Guid.Empty);
            Assert.NotNull(_mockFunctionInstance.Object.Id);

            TimeoutAttribute attribute = typeof(Functions).GetCustomAttribute<TimeoutAttribute>();

            System.Timers.Timer timer = FunctionExecutor.StartFunctionTimeout(_mockFunctionInstance.Object, attribute, _cancellationTokenSource, _traceWriter, null);

            Assert.True(timer.Enabled);
            Assert.Equal(attribute.Timeout.TotalMilliseconds, timer.Interval);

            _mockFunctionInstance.VerifyAll();
        }

        [Fact]
        public async Task InvokeAsync_NoCancellation()
        {
            bool called = false;
            Mock<IFunctionInvoker> mockInvoker = new Mock<IFunctionInvoker>();
            mockInvoker.Setup(i => i.InvokeAsync(It.IsAny<object[]>()))
                .Returns(() =>
                {
                    called = true;
                    return Task.FromResult(0);
                });

            var timeoutSource = new CancellationTokenSource();
            var shutdownSource = new CancellationTokenSource();
            bool throwOnTimeout = true;

            await FunctionExecutor.InvokeAsync(mockInvoker.Object, new object[0], timeoutSource, shutdownSource,
                throwOnTimeout, TimeSpan.MinValue, null);

            Assert.True(called);
        }

        [Fact]
        public async Task InvokeAsync_Timeout_NoThrow()
        {
            bool called = false;
            Mock<IFunctionInvoker> mockInvoker = new Mock<IFunctionInvoker>();
            mockInvoker.Setup(i => i.InvokeAsync(It.IsAny<object[]>()))
                .Returns<object[]>(async (invokeParameters) =>
                {
                    var token = (CancellationToken)invokeParameters[0];
                    while (!token.IsCancellationRequested)
                    {
                        await Task.Delay(1000);
                    }
                    called = true;
                });

            var timeoutSource = new CancellationTokenSource();
            var shutdownSource = new CancellationTokenSource();
            object[] parameters = new object[] { shutdownSource.Token };
            bool throwOnTimeout = false;

            timeoutSource.CancelAfter(500);
            await FunctionExecutor.InvokeAsync(mockInvoker.Object, parameters, timeoutSource, shutdownSource,
                throwOnTimeout, TimeSpan.MinValue, null);

            Assert.True(called);
        }

        [Fact]
        public async Task InvokeAsync_Timeout_Throw()
        {
            Mock<IFunctionInvoker> mockInvoker = new Mock<IFunctionInvoker>();
            mockInvoker.Setup(i => i.InvokeAsync(It.IsAny<object[]>()))
                .Returns(async () =>
                {
                    bool exit = false;
                    Task ignore = Task.Delay(5000).ContinueWith((ct) => exit = true);
                    while (!exit)
                    {
                        await Task.Delay(500);
                    }
                });

            // setup the instance details for the exception message
            MethodInfo method = typeof(Functions).GetMethod("ClassLevel", BindingFlags.Static | BindingFlags.Public);
            _descriptor.Method = method;
            _mockFunctionInstance.SetupGet(p => p.Id).Returns(Guid.Empty);

            var timeoutSource = new CancellationTokenSource();
            var shutdownSource = new CancellationTokenSource();
            object[] parameters = new object[] { shutdownSource.Token };
            bool throwOnTimeout = true;

            TimeSpan timeoutInterval = TimeSpan.FromMilliseconds(500);
            timeoutSource.CancelAfter(timeoutInterval);
            var ex = await Assert.ThrowsAsync<FunctionTimeoutException>(() => FunctionExecutor.InvokeAsync(mockInvoker.Object, parameters, timeoutSource, shutdownSource,
                throwOnTimeout, timeoutInterval, _mockFunctionInstance.Object));

            var expectedMessage = string.Format("Timeout value of {0} was exceeded by function: {1}", timeoutInterval, _mockFunctionInstance.Object.FunctionDescriptor.ShortName);
            Assert.Equal(expectedMessage, ex.Message);
        }

        [Fact]
        public async Task InvokeAsync_Stop_NoTimeout()
        {
            bool called = false;
            Mock<IFunctionInvoker> mockInvoker = new Mock<IFunctionInvoker>();
            mockInvoker.Setup(i => i.InvokeAsync(It.IsAny<object[]>()))
                .Returns<object[]>(async (invokeParameters) =>
                {
                    var token = (CancellationToken)invokeParameters[0];
                    while (!token.IsCancellationRequested)
                    {
                        await Task.Delay(1000);
                    }
                    called = true;
                });

            var timeoutSource = new CancellationTokenSource();
            var shutdownSource = new CancellationTokenSource();
            object[] parameters = new object[] { shutdownSource.Token };
            bool throwOnTimeout = false;

            shutdownSource.CancelAfter(500);
            await FunctionExecutor.InvokeAsync(mockInvoker.Object, parameters, timeoutSource, shutdownSource,
                throwOnTimeout, TimeSpan.MinValue, null);

            Assert.True(called);
        }

        [Fact]
        public async Task InvokeAsync_Stop_Timeout_NoThrow()
        {
            bool called = false;
            Mock<IFunctionInvoker> mockInvoker = new Mock<IFunctionInvoker>();
            mockInvoker.Setup(i => i.InvokeAsync(It.IsAny<object[]>()))
                .Returns<object[]>(async (invokeParameters) =>
                {
                    var token = (CancellationToken)invokeParameters[0];
                    while (!token.IsCancellationRequested)
                    {
                        await Task.Delay(1500);
                    }
                    called = true;
                });

            var timeoutSource = new CancellationTokenSource();
            var shutdownSource = new CancellationTokenSource();
            object[] parameters = new object[] { shutdownSource.Token };
            bool throwOnTimeout = false;

            shutdownSource.CancelAfter(500);
            timeoutSource.CancelAfter(1000);
            await FunctionExecutor.InvokeAsync(mockInvoker.Object, parameters, timeoutSource, shutdownSource,
                throwOnTimeout, TimeSpan.MinValue, null);

            Assert.True(called);
        }

        [Fact]
        public async Task InvokeAsync_Stop_Timeout_Throw()
        {
            Mock<IFunctionInvoker> mockInvoker = new Mock<IFunctionInvoker>();
            mockInvoker.Setup(i => i.InvokeAsync(It.IsAny<object[]>()))
                .Returns(async () =>
                {
                    bool exit = false;
                    Task ignore = Task.Delay(5000).ContinueWith((ct) => exit = true);
                    while (!exit)
                    {
                        await Task.Delay(500);
                    }
                });

            // setup the instance details for the exception message
            MethodInfo method = typeof(Functions).GetMethod("ClassLevel", BindingFlags.Static | BindingFlags.Public);
            _descriptor.Method = method;
            _mockFunctionInstance.SetupGet(p => p.Id).Returns(Guid.Empty);

            var timeoutSource = new CancellationTokenSource();
            var shutdownSource = new CancellationTokenSource();
            object[] parameters = new object[] { shutdownSource.Token };
            bool throwOnTimeout = true;

            TimeSpan timeoutInterval = TimeSpan.FromMilliseconds(1000);
            shutdownSource.CancelAfter(500);
            timeoutSource.CancelAfter(timeoutInterval);
            var ex = await Assert.ThrowsAsync<FunctionTimeoutException>(() => FunctionExecutor.InvokeAsync(mockInvoker.Object, parameters, timeoutSource, shutdownSource,
                 throwOnTimeout, timeoutInterval, _mockFunctionInstance.Object));

            var expectedMessage = string.Format("Timeout value of {0} was exceeded by function: {1}", timeoutInterval, _mockFunctionInstance.Object.FunctionDescriptor.ShortName);
            Assert.Equal(expectedMessage, ex.Message);
        }

        [Fact]
        public void OnFunctionTimeout_PerformsExpectedOperations()
        {
            RunOnFunctionTimeoutTest(false, "Initiating cancellation.");
        }

        [Fact]
        public void OnFunctionTimeout_DoesNotCancel_IfDebugging()
        {
            RunOnFunctionTimeoutTest(true, "Function will not be cancelled while debugging.");
        }

        private void RunOnFunctionTimeoutTest(bool isDebugging, string expectedMessage)
        {
            System.Timers.Timer timer = new System.Timers.Timer(TimeSpan.FromMinutes(1).TotalMilliseconds);
            timer.Start();

            Assert.True(timer.Enabled);
            Assert.False(_cancellationTokenSource.IsCancellationRequested);

            MethodInfo method = typeof(Functions).GetMethod("MethodLevel", BindingFlags.Static | BindingFlags.Public);
            TimeoutAttribute attribute = method.GetCustomAttribute<TimeoutAttribute>();
            Guid instanceId = Guid.Parse("B2D1DD72-80E2-412B-A22E-3B4558F378B4");
            bool timeoutWhileDebugging = false;

            TestLogger logger = new TestLogger("Tests.FunctionExecutor");

            FunctionExecutor.OnFunctionTimeout(timer, method, instanceId, attribute.Timeout, timeoutWhileDebugging, _traceWriter, logger, _cancellationTokenSource, () => isDebugging);

            Assert.False(timer.Enabled);
            Assert.NotEqual(isDebugging, _cancellationTokenSource.IsCancellationRequested);

            string message = string.Format("Timeout value of 00:01:00 exceeded by function 'Functions.MethodLevel' (Id: 'b2d1dd72-80e2-412b-a22e-3b4558f378b4'). {0}", expectedMessage);

            // verify TraceWriter
            TraceEvent trace = _traceWriter.Traces[0];
            Assert.Equal(TraceLevel.Error, trace.Level);
            Assert.Equal(TraceSource.Execution, trace.Source);
            Assert.Equal(message, trace.Message);

            // verify ILogger
            LogMessage log = logger.LogMessages.Single();
            Assert.Equal(LogLevel.Error, log.Level);
            Assert.Equal(message, log.FormattedMessage);
        }

        public static void GlobalLevel(CancellationToken cancellationToken)
        {
        }

        [Timeout("00:02:00", ThrowOnTimeout = true, TimeoutWhileDebugging = true)]
        public static class Functions
        {
            [Timeout("00:01:00", ThrowOnTimeout = true, TimeoutWhileDebugging = true)]
            public static void MethodLevel(CancellationToken cancellationToken)
            {
            }

            public static void ClassLevel(CancellationToken cancellationToken)
            {
            }

            public static void NoCancellationTokenParameter()
            {
            }

            [TraceLevel(TraceLevel.Off)]
            public static void TraceLevelOverride_Off()
            {
            }

            [TraceLevel(TraceLevel.Error)]
            public static void TraceLevelOverride_Error()
            {
            }
        }
    }
}
