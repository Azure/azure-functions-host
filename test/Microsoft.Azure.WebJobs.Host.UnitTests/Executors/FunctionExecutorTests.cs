// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
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
        private readonly TimeSpan _globalFunctionTimeout = TimeSpan.FromMinutes(3);

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

            System.Timers.Timer timer = FunctionExecutor.StartFunctionTimeout(_mockFunctionInstance.Object, _globalFunctionTimeout, _cancellationTokenSource, _traceWriter);

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

            System.Timers.Timer timer = FunctionExecutor.StartFunctionTimeout(_mockFunctionInstance.Object, _globalFunctionTimeout, _cancellationTokenSource, _traceWriter);

            Assert.True(timer.Enabled);
            Assert.Equal(attribute.Timeout.TotalMilliseconds, timer.Interval);

            _mockFunctionInstance.VerifyAll();
        }

        [Fact]
        public void StartFunctionTimeout_GlobalTimeout_CreatesExpectedTimer()
        {
            MethodInfo method = GetType().GetMethod("GlobalLevel", BindingFlags.Static | BindingFlags.Public);
            _descriptor.Method = method;
            TimeoutAttribute attribute = method.GetCustomAttribute<TimeoutAttribute>();

            System.Timers.Timer timer = FunctionExecutor.StartFunctionTimeout(_mockFunctionInstance.Object, _globalFunctionTimeout, _cancellationTokenSource, _traceWriter);

            Assert.True(timer.Enabled);
            Assert.Equal(_globalFunctionTimeout.TotalMilliseconds, timer.Interval);

            _mockFunctionInstance.VerifyAll();
        }

        [Fact]
        public void StartFunctionTimeout_NoTimeout_ReturnsNull()
        {
            MethodInfo method = GetType().GetMethod("GlobalLevel", BindingFlags.Static | BindingFlags.Public);
            _descriptor.Method = method;

            System.Timers.Timer timer = FunctionExecutor.StartFunctionTimeout(_mockFunctionInstance.Object, null, _cancellationTokenSource, _traceWriter);

            Assert.Null(timer);

            _mockFunctionInstance.VerifyAll();
        }

        [Fact]
        public void StartFunctionTimeout_NoCancellationTokenParameter_ReturnsNull()
        {
            MethodInfo method = typeof(Functions).GetMethod("NoCancellationTokenParameter", BindingFlags.Static | BindingFlags.Public);
            _descriptor.Method = method;

            System.Timers.Timer timer = FunctionExecutor.StartFunctionTimeout(_mockFunctionInstance.Object, _globalFunctionTimeout, _cancellationTokenSource, _traceWriter);

            Assert.Null(timer);

            _mockFunctionInstance.VerifyAll();
        }

        [Fact]
        public void OnFunctionTimeout_PerformsExpectedOperations()
        {
            System.Timers.Timer timer = new System.Timers.Timer(TimeSpan.FromMinutes(1).TotalMilliseconds);
            timer.Start();

            Assert.True(timer.Enabled);
            Assert.False(_cancellationTokenSource.IsCancellationRequested);

            MethodInfo method = typeof(Functions).GetMethod("MethodLevel", BindingFlags.Static | BindingFlags.Public);
            TimeoutAttribute attribute = method.GetCustomAttribute<TimeoutAttribute>();
            Guid instanceId = Guid.Parse("B2D1DD72-80E2-412B-A22E-3B4558F378B4");
            FunctionExecutor.OnFunctionTimeout(timer, method, instanceId, attribute.Timeout, _traceWriter, _cancellationTokenSource);

            Assert.False(timer.Enabled);
            Assert.True(_cancellationTokenSource.IsCancellationRequested);

            TraceEvent trace = _traceWriter.Traces[0];
            Assert.Equal(TraceLevel.Error, trace.Level);
            Assert.Equal(TraceSource.Execution, trace.Source);
            Assert.Equal("Timeout value of 00:01:00 exceeded by function 'Functions.MethodLevel' (Id: 'b2d1dd72-80e2-412b-a22e-3b4558f378b4'). Initiating cancellation.", trace.Message);
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

        [Timeout("00:02:00")]
        public static class Functions
        {
            [Timeout("00:01:00")]
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
