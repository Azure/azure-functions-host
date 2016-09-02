// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.TestCommon;
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
            TimeoutAttribute attribute = method.GetCustomAttribute<TimeoutAttribute>();

            System.Timers.Timer timer = FunctionExecutor.StartFunctionTimeout(_mockFunctionInstance.Object, attribute, _cancellationTokenSource, _traceWriter);

            Assert.True(timer.Enabled);
            Assert.Equal(attribute.Timeout.TotalMilliseconds, timer.Interval);

            _mockFunctionInstance.VerifyAll();
        }

        [Fact]
        public void StartFunctionTimeout_ClassLevelTimeout_CreatesExpectedTimer()
        {
            MethodInfo method = typeof(Functions).GetMethod("ClassLevel", BindingFlags.Static | BindingFlags.Public);
            _descriptor.Method = method;
            TimeoutAttribute attribute = typeof(Functions).GetCustomAttribute<TimeoutAttribute>();

            System.Timers.Timer timer = FunctionExecutor.StartFunctionTimeout(_mockFunctionInstance.Object, attribute, _cancellationTokenSource, _traceWriter);

            Assert.True(timer.Enabled);
            Assert.Equal(attribute.Timeout.TotalMilliseconds, timer.Interval);

            _mockFunctionInstance.VerifyAll();
        }

        [Fact]
        public void StartFunctionTimeout_NoTimeout_ReturnsNull()
        {
            TimeoutAttribute timeoutAttribute = null;
            System.Timers.Timer timer = FunctionExecutor.StartFunctionTimeout(null, timeoutAttribute, _cancellationTokenSource, _traceWriter);

            Assert.Null(timer);
        }

        [Fact]
        public void StartFunctionTimeout_NoCancellationTokenParameter_ThrowOnTimeoutFalse_ReturnsNull()
        {
            MethodInfo method = typeof(Functions).GetMethod("NoCancellationTokenParameter", BindingFlags.Static | BindingFlags.Public);
            _descriptor.Method = method;

            TimeoutAttribute attribute = typeof(Functions).GetCustomAttribute<TimeoutAttribute>();
            attribute.ThrowOnTimeout = false;

            System.Timers.Timer timer = FunctionExecutor.StartFunctionTimeout(_mockFunctionInstance.Object, attribute, _cancellationTokenSource, _traceWriter);

            Assert.Null(timer);

            _mockFunctionInstance.VerifyAll();
        }

        [Fact]
        public void StartFunctionTimeout_NoCancellationTokenParameter_ThrowOnTimeoutTrue_CreatesExpectedTimer()
        {
            MethodInfo method = typeof(Functions).GetMethod("NoCancellationTokenParameter", BindingFlags.Static | BindingFlags.Public);
            _descriptor.Method = method;
            TimeoutAttribute attribute = typeof(Functions).GetCustomAttribute<TimeoutAttribute>();

            System.Timers.Timer timer = FunctionExecutor.StartFunctionTimeout(_mockFunctionInstance.Object, attribute, _cancellationTokenSource, _traceWriter);

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
            bool throwOnTimeout = false;

            await FunctionExecutor.InvokeAsync(mockInvoker.Object, new object[0], timeoutSource, shutdownSource, throwOnTimeout);

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
                    await Task.Yield();
                    var token = (CancellationToken)invokeParameters[0];
                    while (!token.IsCancellationRequested)
                    {
                        Thread.Sleep(1000);
                    }
                    called = true;
                });

            var timeoutSource = new CancellationTokenSource();
            var shutdownSource = new CancellationTokenSource();
            object[] parameters = new object[] { shutdownSource.Token };
            bool throwOnTimeout = false;

            timeoutSource.CancelAfter(500);
            await FunctionExecutor.InvokeAsync(mockInvoker.Object, parameters, timeoutSource, shutdownSource, throwOnTimeout);

            Assert.True(called);
        }

        [Fact]
        public async Task InvokeAsync_Timeout_Throw()
        {
            Mock<IFunctionInvoker> mockInvoker = new Mock<IFunctionInvoker>();
            mockInvoker.Setup(i => i.InvokeAsync(It.IsAny<object[]>()))
                .Returns(async () =>
                {
                    await Task.Yield();
                    while (true)
                    {
                    }
                });

            var timeoutSource = new CancellationTokenSource();
            var shutdownSource = new CancellationTokenSource();
            object[] parameters = new object[] { shutdownSource.Token };
            bool throwOnTimeout = true;

            timeoutSource.CancelAfter(500);
            await Assert.ThrowsAsync<TaskCanceledException>(() => FunctionExecutor.InvokeAsync(mockInvoker.Object, parameters, timeoutSource, shutdownSource, throwOnTimeout));
        }

        [Fact]
        public async Task InvokeAsync_Stop_NoTimeout()
        {
            bool called = false;
            Mock<IFunctionInvoker> mockInvoker = new Mock<IFunctionInvoker>();
            mockInvoker.Setup(i => i.InvokeAsync(It.IsAny<object[]>()))
                .Returns<object[]>(async (invokeParameters) =>
                {
                    await Task.Yield();
                    var token = (CancellationToken)invokeParameters[0];
                    while (!token.IsCancellationRequested)
                    {
                        Thread.Sleep(1000);
                    }
                    called = true;
                });

            var timeoutSource = new CancellationTokenSource();
            var shutdownSource = new CancellationTokenSource();
            object[] parameters = new object[] { shutdownSource.Token };
            bool throwOnTimeout = false;

            shutdownSource.CancelAfter(500);
            await FunctionExecutor.InvokeAsync(mockInvoker.Object, parameters, timeoutSource, shutdownSource, throwOnTimeout);

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
                    await Task.Yield();
                    var token = (CancellationToken)invokeParameters[0];
                    while (!token.IsCancellationRequested)
                    {
                        Thread.Sleep(1500);
                    }
                    called = true;
                });

            var timeoutSource = new CancellationTokenSource();
            var shutdownSource = new CancellationTokenSource();
            object[] parameters = new object[] { shutdownSource.Token };
            bool throwOnTimeout = false;

            shutdownSource.CancelAfter(500);
            timeoutSource.CancelAfter(1000);
            await FunctionExecutor.InvokeAsync(mockInvoker.Object, parameters, timeoutSource, shutdownSource, throwOnTimeout);

            Assert.True(called);
        }

        [Fact]
        public async Task InvokeAsync_Stop_Timeout_Throw()
        {
            Mock<IFunctionInvoker> mockInvoker = new Mock<IFunctionInvoker>();
            mockInvoker.Setup(i => i.InvokeAsync(It.IsAny<object[]>()))
                .Returns(async () =>
                {
                    await Task.Yield();
                    while (true)
                    {
                    }
                });

            var timeoutSource = new CancellationTokenSource();
            var shutdownSource = new CancellationTokenSource();
            object[] parameters = new object[] { shutdownSource.Token };
            bool throwOnTimeout = true;

            shutdownSource.CancelAfter(500);
            timeoutSource.CancelAfter(1000);
            await Assert.ThrowsAsync<TaskCanceledException>(() => FunctionExecutor.InvokeAsync(mockInvoker.Object, parameters, timeoutSource, shutdownSource, throwOnTimeout));
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
            FunctionExecutor.OnFunctionTimeout(timer, method, instanceId, attribute.Timeout, timeoutWhileDebugging, _traceWriter, _cancellationTokenSource, () => isDebugging);

            Assert.False(timer.Enabled);
            Assert.NotEqual(isDebugging, _cancellationTokenSource.IsCancellationRequested);

            TraceEvent trace = _traceWriter.Traces[0];
            Assert.Equal(TraceLevel.Error, trace.Level);
            Assert.Equal(TraceSource.Execution, trace.Source);
            string message = string.Format("Timeout value of 00:01:00 exceeded by function 'Functions.MethodLevel' (Id: 'b2d1dd72-80e2-412b-a22e-3b4558f378b4'). {0}", expectedMessage);
            Assert.Equal(message, trace.Message);
        }

        [Fact]
        public void GetFunctionTraceLevel_ReturnsExpectedLevel()
        {
            _descriptor.Method = typeof(Functions).GetMethod("MethodLevel", BindingFlags.Static | BindingFlags.Public);
            TraceLevel result = FunctionExecutor.GetFunctionTraceLevel(_mockFunctionInstance.Object);
            Assert.Equal(TraceLevel.Verbose, result);

            _descriptor.Method = typeof(Functions).GetMethod("TraceLevelOverride_Off", BindingFlags.Static | BindingFlags.Public);
            result = FunctionExecutor.GetFunctionTraceLevel(_mockFunctionInstance.Object);
            Assert.Equal(TraceLevel.Off, result);

            _descriptor.Method = typeof(Functions).GetMethod("TraceLevelOverride_Error", BindingFlags.Static | BindingFlags.Public);
            result = FunctionExecutor.GetFunctionTraceLevel(_mockFunctionInstance.Object);
            Assert.Equal(TraceLevel.Error, result);
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
