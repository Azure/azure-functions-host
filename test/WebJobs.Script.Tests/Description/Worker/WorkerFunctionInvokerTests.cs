// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Abstractions.Description;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.WebJobs.Script.Tests;
using Moq;
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

            FunctionMetadata metaData = new FunctionMetadata();
            _testFunctionInvoker = new TestWorkerFunctionInvoker(sc, null, metaData, NullLoggerFactory.Instance, null, new Collection<FunctionBinding>(), _mockFunctionInvocationDispatcher.Object, _applicationLifetime.Object);
        }

        [Fact]
        public async Task InvokeTimeout_CallsShutdown()
        {
            try
            {
                _mockFunctionInvocationDispatcher.Setup(a => a.State).Returns(FunctionInvocationDispatcherState.Initializing);
                await Task.WhenAny(_testFunctionInvoker.InvokeCore(new object[] { }, null), Task.Delay(TimeSpan.FromSeconds(500)));
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
        public async Task FunctionDispatcher_DelaysInvoke_WhenNotReady(FunctionInvocationDispatcherState state, bool delaysExecution)
        {
            _mockFunctionInvocationDispatcher.Setup(a => a.State).Returns(state);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
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
    }
}
