// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Metrics;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class WorkerFunctionInvokerTests
    {
        private readonly TestWorkerFunctionInvoker _testFunctionInvoker;
        private readonly Mock<IScriptApplicationLifetime> _applicationLifetime;
        private readonly Mock<IFunctionInvocationDispatcher> _mockFunctionInvocationDispatcher;

        public WorkerFunctionInvokerTests()
        {
            _applicationLifetime = new Mock<IScriptApplicationLifetime>();
            _mockFunctionInvocationDispatcher = new Mock<IFunctionInvocationDispatcher>();
            _mockFunctionInvocationDispatcher.Setup(a => a.ErrorEventsThreshold).Returns(0);

            var hostBuilder = new HostBuilder()
                .ConfigureDefaultTestWebScriptHost(o =>
                {
                    o.ScriptPath = TestHelpers.FunctionsTestDirectory;
                    o.LogPath = TestHelpers.GetHostLogFileDirectory().Parent.FullName;
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddMetrics();
                    services.AddSingleton<IEnvironment>(new TestEnvironment());
                    services.AddSingleton<IHostMetrics, HostMetrics>();
                });

            var host = hostBuilder.Build();

            var sc = host.GetScriptHost();

            FunctionMetadata metaData = new FunctionMetadata();
            BindingMetadata bindingMetadata = new BindingMetadata();
            bindingMetadata.Name = "TestName";
            _testFunctionInvoker = new TestWorkerFunctionInvoker(sc, bindingMetadata, metaData, NullLoggerFactory.Instance, new Collection<FunctionBinding>(), new Collection<FunctionBinding>(),
                _mockFunctionInvocationDispatcher.Object, _applicationLifetime.Object, TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task InvokeTimeout_CallsShutdown()
        {
            try
            {
                _mockFunctionInvocationDispatcher.Setup(a => a.State).Returns(FunctionInvocationDispatcherState.Initializing);
                await Task.WhenAny(_testFunctionInvoker.InvokeCore(new object[] { }, null), Task.Delay(TimeSpan.FromSeconds(30)));
            }
            catch (Exception)
            {
            }
            _applicationLifetime.Verify(a => a.StopApplication(), Times.Once);
        }

        [Theory]
        [InlineData(FunctionInvocationDispatcherState.Default, false)]
        [InlineData(FunctionInvocationDispatcherState.Initializing, true)]
        [InlineData(FunctionInvocationDispatcherState.Initialized, false)]
        [InlineData(FunctionInvocationDispatcherState.WorkerProcessRestarting, true)]
        [InlineData(FunctionInvocationDispatcherState.Disposing, true)]
        [InlineData(FunctionInvocationDispatcherState.Disposed, true)]
        internal async Task FunctionDispatcher_DelaysInvoke_WhenNotReady(FunctionInvocationDispatcherState state, bool delaysExecution)
        {
            _mockFunctionInvocationDispatcher.Setup(a => a.State).Returns(state);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(2));
            var invokeCoreTask = _testFunctionInvoker.InvokeCore(new object[] { }, null);
            var result = await Task.WhenAny(invokeCoreTask, timeoutTask);
            if (delaysExecution)
            {
                Assert.Equal(timeoutTask, result);
            }
            else
            {
                Assert.Equal(invokeCoreTask, result);
            }
        }

        [Fact]
        public async Task InvokeInitialized_DoesNotCallShutdown()
        {
            try
            {
                _mockFunctionInvocationDispatcher.Setup(a => a.State).Returns(FunctionInvocationDispatcherState.Initialized);
                await Task.WhenAny(_testFunctionInvoker.InvokeCore(new object[] { }, null), Task.Delay(TimeSpan.FromSeconds(125)));
            }
            catch (Exception)
            {
            }
            _applicationLifetime.Verify(a => a.StopApplication(), Times.Never);
        }

        // Regression test for the worker-channel shutdown hang (GitHub azure-functions-host#10683).
        //
        // WorkerChannel.Shutdown() fails every in-flight invocation by completing its ResultSource - the
        // TaskCompletionSource that WorkerFunctionInvoker creates here and awaits. If that source is created
        // without TaskCreationOptions.RunContinuationsAsynchronously, completing it runs the awaiting
        // continuation (the resumed function pipeline) inline on the completing thread. When Shutdown()
        // completes it inline and that continuation blocks, the shutdown thread deadlocks, so
        // ShutdownAndDispose never returns and the dispatcher is left with zero worker channels until a
        // manual restart.
        //
        // This test captures the real ResultSource created by WorkerFunctionInvoker, attaches a blocking
        // continuation, and asserts that completing the source (as Shutdown does) returns promptly instead of
        // running the continuation inline. It fails (times out) unless the ResultSource is created with
        // RunContinuationsAsynchronously.
        [Fact]
        public async Task InvokeCore_ResultSource_DoesNotRunContinuationsInline()
        {
            TaskCompletionSource<ScriptInvocationResult> capturedResultSource = null;

            _mockFunctionInvocationDispatcher
                .Setup(d => d.InvokeAsync(It.IsAny<ScriptInvocationContext>()))
                .Callback<ScriptInvocationContext>(ctx => capturedResultSource = ctx.ResultSource)
                .Returns(Task.CompletedTask);
            _mockFunctionInvocationDispatcher.Setup(a => a.State).Returns(FunctionInvocationDispatcherState.Initialized);

            // Start the invocation. WorkerFunctionInvoker creates the ResultSource, hands the context to the
            // (mock) dispatcher - where we capture it - then awaits ResultSource.Task.
            var invokeTask = _testFunctionInvoker.InvokeCore(
                new object[] { null, null, null, null, default(CancellationToken) },
                new FunctionInvocationContext
                {
                    Binder = new Mock<Binder>().Object,
                    ExecutionContext = new ExecutionContext { InvocationId = Guid.NewGuid() }
                });

            Assert.True(
                SpinWait.SpinUntil(() => capturedResultSource != null, TimeSpan.FromSeconds(5)),
                "WorkerFunctionInvoker did not create/hand off the invocation ResultSource.");

            // Model the resumed function pipeline: a continuation of ResultSource.Task that blocks. Using
            // ExecuteSynchronously makes it eligible to run inline on the completing thread - which is exactly
            // what a default TaskCompletionSource permits and what RunContinuationsAsynchronously prevents.
            using var continuationEntered = new ManualResetEventSlim(false);
            using var releaseContinuation = new ManualResetEventSlim(false);
            var blockingContinuation = capturedResultSource.Task.ContinueWith(
                _ =>
                {
                    continuationEntered.Set();
                    releaseContinuation.Wait(TimeSpan.FromSeconds(30));
                },
                TaskContinuationOptions.ExecuteSynchronously);

            // Complete the invocation the way WorkerChannel.Shutdown does. If the continuation runs inline,
            // this call blocks until releaseContinuation is set (i.e. it never returns within the timeout).
            var completer = Task.Run(() =>
                capturedResultSource.TrySetException(new InvalidOperationException("Worker channel is shutting down.")));

            bool completedPromptly = completer.Wait(TimeSpan.FromSeconds(10));

            // Release the (possibly still-blocked) continuation so the test can clean up regardless of outcome.
            releaseContinuation.Set();
            await blockingContinuation;
            await Assert.ThrowsAnyAsync<Exception>(() => invokeTask);

            const string failureMessage =
                "Completing the invocation ResultSource ran a blocking continuation inline on the completing " +
                "thread. WorkerFunctionInvoker must create the ResultSource with " +
                "TaskCreationOptions.RunContinuationsAsynchronously so WorkerChannel.Shutdown cannot hang.";
            Assert.True(completedPromptly, failureMessage);
        }

        [Fact]
        public async Task InvokeCore_DoesNotAddReturn_WhenOutputsIsImmutableDictionary()
        {
            // Arrange
            var mockBinder = new Mock<Binder>();
            var immutableOutputs = System.Collections.Immutable.ImmutableDictionary<string, object>.Empty;
            var invocationResult = new ScriptInvocationResult
            {
                Outputs = immutableOutputs,
            };

            // Setup the dispatcher to complete the invocation with our result
            _mockFunctionInvocationDispatcher
                .Setup(d => d.InvokeAsync(It.IsAny<ScriptInvocationContext>()))
                .Callback<ScriptInvocationContext>(ctx =>
                {
                    ctx.ResultSource.SetResult(invocationResult);
                })
                .Returns(Task.CompletedTask);

            _mockFunctionInvocationDispatcher.Setup(a => a.State).Returns(FunctionInvocationDispatcherState.Initialized);

            // Act
            var result = await _testFunctionInvoker.InvokeCore(
                new object[] { null, null, null, null, default(System.Threading.CancellationToken) },
                new FunctionInvocationContext
                {
                    Binder = mockBinder.Object,
                    ExecutionContext = new ExecutionContext
                    {
                        InvocationId = Guid.NewGuid()
                    }
                });

            // Assert
            Assert.IsType<System.Collections.Immutable.ImmutableDictionary<string, object>>(invocationResult.Outputs);
            Assert.False(invocationResult.Outputs.ContainsKey("$return"));
        }
    }
}
