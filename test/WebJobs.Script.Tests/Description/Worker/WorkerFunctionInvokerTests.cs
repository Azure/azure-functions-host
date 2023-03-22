// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;
using IApplicationLifetime = Microsoft.AspNetCore.Hosting.IApplicationLifetime;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class WorkerFunctionInvokerTests
    {
        private readonly TestWorkerFunctionInvoker _testFunctionInvoker;
        private readonly Mock<IApplicationLifetime> _applicationLifetime;
        private readonly Mock<IFunctionInvocationDispatcher> _mockFunctionInvocationDispatcher;

        public WorkerFunctionInvokerTests()
        {
            _applicationLifetime = new Mock<IApplicationLifetime>();
            _mockFunctionInvocationDispatcher = new Mock<IFunctionInvocationDispatcher>();
            _mockFunctionInvocationDispatcher.Setup(a => a.ErrorEventsThreshold).Returns(0);

            var hostBuilder = new HostBuilder()
                .ConfigureDefaultTestWebScriptHost(o =>
                {
                    o.ScriptPath = TestHelpers.FunctionsTestDirectory;
                    o.LogPath = TestHelpers.GetHostLogFileDirectory().Parent.FullName;
                });
            var host = hostBuilder.Build();

            var sc = host.GetScriptHost();

            FunctionMetadata functionMetadata = new();
            BindingMetadata bindingMetadata = CreateTestBindingMetadata();

            _testFunctionInvoker = new TestWorkerFunctionInvoker(sc, bindingMetadata, functionMetadata, NullLoggerFactory.Instance, new Collection<FunctionBinding>(), new Collection<FunctionBinding>(),
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

        [Fact]
        public async Task Invoke_ResultSourceCanceled_ThrowsFunctionInvocationCanceledException()
        {
            try
            {
                var cts = new CancellationTokenSource();
                var testParams = new object[] { null, null, null, null,  cts.Token };
                var testContext = CreateTestFunctionInvocationContext();

                _mockFunctionInvocationDispatcher.Setup(a => a.State).Returns(FunctionInvocationDispatcherState.Initialized);
                _mockFunctionInvocationDispatcher
                            .Setup(a => a.InvokeAsync(It.IsAny<ScriptInvocationContext>()))
                            .Returns(Task.FromException(new TaskCanceledException()));

                await _testFunctionInvoker.InvokeCore(testParams, testContext);
            }
            catch (Exception ex)
            {
                Assert.Equal(typeof(FunctionInvocationCanceledException), ex.GetType());
            }
        }

        private FunctionInvocationContext CreateTestFunctionInvocationContext()
        {
            var exeContext = new ExecutionContext { InvocationId = Guid.NewGuid() };
            var mockBinder = new Mock<Binder>();
            mockBinder.Setup(m => m.BindingData).Returns(new Dictionary<string, object>());

            return new FunctionInvocationContext()
            {
                ExecutionContext = exeContext,
                Binder = mockBinder.Object,
                Logger = It.IsAny<ILogger>()
            };
        }

        private BindingMetadata CreateTestBindingMetadata()
        {
            JObject binding = JObject.FromObject(new
            {
                type = "manualTrigger",
                name = "manual",
                direction = "in"
            });
            return BindingMetadata.Create(binding);
        }

        [Theory]
        [InlineData(FunctionInvocationDispatcherState.Default, false)]
        [InlineData(FunctionInvocationDispatcherState.Initializing, true)]
        [InlineData(FunctionInvocationDispatcherState.Initialized, false)]
        [InlineData(FunctionInvocationDispatcherState.WorkerProcessRestarting, true)]
        [InlineData(FunctionInvocationDispatcherState.Disposing, true)]
        [InlineData(FunctionInvocationDispatcherState.Disposed, true)]
        public async Task FunctionDispatcher_DelaysInvoke_WhenNotReady(FunctionInvocationDispatcherState state, bool delaysExecution)
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
    }
}
