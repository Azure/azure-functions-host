// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.ExternalWorkers
{
    public class ConnectedWorkerInvocationDispatcherTests
    {
        private readonly Mock<IConnectedWorkerChannelManager> _mockChannelManager = new();
        private readonly ConnectedWorkerInvocationDispatcher _dispatcher;

        public ConnectedWorkerInvocationDispatcherTests()
        {
            _mockChannelManager.Setup(m => m.GetChannels())
                .Returns(new Dictionary<string, ConnectedWorkerChannel>());

            _dispatcher = new ConnectedWorkerInvocationDispatcher(
                _mockChannelManager.Object,
                NullLogger<ConnectedWorkerInvocationDispatcher>.Instance);
        }

        private static ConnectedWorkerChannel CreateTestChannel(string workerId)
        {
            var eventManager = new ScriptEventManager();
            eventManager.AddGrpcChannels(workerId);

            var workerConfig = TestHelpers.GetTestWorkerConfigs().First();
            var mockHostOptions = new Mock<IOptionsMonitor<ScriptApplicationHostOptions>>();
            mockHostOptions.Setup(o => o.CurrentValue).Returns(new ScriptApplicationHostOptions { ScriptPath = @"c:\testdir" });

            return new ConnectedWorkerChannel(
                workerId,
                eventManager,
                new Mock<IScriptHostManager>().Object,
                workerConfig,
                NullLogger.Instance,
                new Mock<IMetricsLogger>().Object,
                new TestEnvironment(),
                mockHostOptions.Object,
                new Mock<ISharedMemoryManager>().Object,
                Options.Create(new WorkerConcurrencyOptions()),
                Options.Create(new FunctionsHostingConfigOptions()),
                new Mock<IHttpProxyService>().Object);
        }

        private static ConnectedWorkerChannel CreateReadyChannel(string workerId)
        {
            var channel = CreateTestChannel(workerId);
            SetChannelReady(channel);

            return channel;
        }

        private static ConnectedWorkerChannel CreateReadyChannelWithBuffers(string workerId, IEnumerable<FunctionMetadata> functions)
        {
            var channel = CreateReadyChannel(workerId);
            channel.SetupFunctionInvocationBuffers(functions);

            return channel;
        }

        private static void SetChannelReady(ConnectedWorkerChannel channel)
        {
            // Use reflection to set the internal state flags so IsChannelReadyForInvocations() returns true.
            // RpcWorkerChannelState.InvocationBuffersInitialized (1 << 1) | Initialized (1 << 3)
            var stateField = typeof(ConnectedWorkerChannel).BaseType
                .GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
            var readyValue = Enum.ToObject(stateField.FieldType, (1 << 1) | (1 << 3));
            stateField.SetValue(channel, readyValue);
        }

        private static ScriptInvocationContext CreateInvocationContext(FunctionMetadata function)
        {
            return new ScriptInvocationContext
            {
                FunctionMetadata = function,
                ResultSource = new TaskCompletionSource<ScriptInvocationResult>()
            };
        }

        [Fact]
        public void State_ReturnsInitializing_WhenNoChannels()
        {
            Assert.Equal(FunctionInvocationDispatcherState.Initializing, _dispatcher.State);
        }

        [Fact]
        public void State_ReturnsInitialized_WhenChannelReady()
        {
            var channel = CreateReadyChannel("worker1");
            _mockChannelManager.Setup(m => m.GetChannels())
                .Returns(new Dictionary<string, ConnectedWorkerChannel> { ["worker1"] = channel });

            Assert.Equal(FunctionInvocationDispatcherState.Initialized, _dispatcher.State);
        }

        [Fact]
        public async Task InvokeAsync_ThrowsWhenNoReadyChannel()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _dispatcher.InvokeAsync(null));
        }

        [Fact]
        public async Task InvokeAsync_RoundRobinDistribution()
        {
            var function = new FunctionMetadata { Name = "TestFunction" };
            var functionId = function.GetFunctionId();

            var channels = new Dictionary<string, ConnectedWorkerChannel>();
            for (int i = 0; i < 3; i++)
            {
                var ch = CreateReadyChannelWithBuffers($"worker{i}", new[] { function });
                channels[$"worker{i}"] = ch;
            }

            _mockChannelManager.Setup(m => m.GetChannels()).Returns(channels);

            for (int i = 0; i < 6; i++)
            {
                await _dispatcher.InvokeAsync(CreateInvocationContext(function));
            }

            foreach (var ch in channels.Values)
            {
                Assert.Equal(2, ch.FunctionInputBuffers[functionId].Count);
            }
        }

        [Fact]
        public async Task InvokeAsync_UsesFunctionInputBuffer()
        {
            var function = new FunctionMetadata { Name = "TestFunction" };
            var functionId = function.GetFunctionId();

            var channel = CreateReadyChannelWithBuffers("worker1", new[] { function });
            _mockChannelManager.Setup(m => m.GetChannels())
                .Returns(new Dictionary<string, ConnectedWorkerChannel> { ["worker1"] = channel });

            var context = CreateInvocationContext(function);
            await _dispatcher.InvokeAsync(context);

            Assert.True(channel.FunctionInputBuffers[functionId].TryReceive(out var received));
            Assert.Same(context, received);
        }

        [Fact]
        public async Task InvokeAsync_ThrowsWhenFunctionNotLoaded()
        {
            var channel = CreateReadyChannel("worker1");
            _mockChannelManager.Setup(m => m.GetChannels())
                .Returns(new Dictionary<string, ConnectedWorkerChannel> { ["worker1"] = channel });

            var function = new FunctionMetadata { Name = "NonExistentFunction" };
            var context = CreateInvocationContext(function);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _dispatcher.InvokeAsync(context));
        }

        [Fact]
        public async Task InitializeAsync_SetsFunctionBuffersOnChannels()
        {
            var function = new FunctionMetadata { Name = "TestFunction" };
            var functionId = function.GetFunctionId();

            var channels = new Dictionary<string, ConnectedWorkerChannel>
            {
                ["worker1"] = CreateTestChannel("worker1"),
                ["worker2"] = CreateTestChannel("worker2")
            };

            _mockChannelManager.Setup(m => m.GetChannels()).Returns(channels);

            await _dispatcher.InitializeAsync(new[] { function });

            foreach (var ch in channels.Values)
            {
                Assert.True(ch.FunctionInputBuffers.ContainsKey(functionId));
            }
        }

        [Fact]
        public async Task InitializeAsync_NewChannelAfterInit_GetsSetup()
        {
            var function = new FunctionMetadata { Name = "TestFunction" };
            var functionId = function.GetFunctionId();

            _mockChannelManager.Setup(m => m.GetChannels())
                .Returns(new Dictionary<string, ConnectedWorkerChannel>());

            await _dispatcher.InitializeAsync(new[] { function });

            var newChannel = CreateTestChannel("worker-new");
            _dispatcher.SetupChannel(newChannel);

            Assert.True(newChannel.FunctionInputBuffers.ContainsKey(functionId));
        }

        [Fact]
        public async Task RestartWorkerWithInvocationIdAsync_ReturnsFalse()
        {
            var result = await _dispatcher.RestartWorkerWithInvocationIdAsync("invocation1", new Exception("test"));

            Assert.False(result);
        }

        [Fact]
        public void ErrorEventsThreshold_ReturnsMaxValue()
        {
            Assert.Equal(int.MaxValue, _dispatcher.ErrorEventsThreshold);
        }
    }
}
