// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class RpcScriptHostWorkerManagerTests
    {
        [Fact]
        public void GetWorkerRuntimeInfo_ReturnsDistinctReportsFromCompletedChannels()
        {
            var nodeInfo = new WorkerRuntimeInfo("node", "22.11.0", "3.10.1");
            var jobHostChannel = new TestRpcWorkerChannel("jobhost") { ReportedRuntimeInfo = nodeInfo };
            var duplicateChannel = new TestRpcWorkerChannel("duplicate") { ReportedRuntimeInfo = new WorkerRuntimeInfo("node", "22.11.0", "3.10.1") };
            var unreportedChannel = new TestRpcWorkerChannel("unreported");
            var completedWebHostChannel = new TestRpcWorkerChannel("webhost") { ReportedRuntimeInfo = new WorkerRuntimeInfo("node", "22.12.0", "3.10.1") };

            var completed = new TaskCompletionSource<IRpcWorkerChannel>();
            completed.SetResult(completedWebHostChannel);
            var pending = new TaskCompletionSource<IRpcWorkerChannel>();

            var jobHostManager = new Mock<IJobHostRpcWorkerChannelManager>(MockBehavior.Strict);
            jobHostManager.Setup(p => p.GetChannels()).Returns([jobHostChannel, duplicateChannel, unreportedChannel]);

            var webHostManager = new Mock<IWebHostRpcWorkerChannelManager>(MockBehavior.Strict);
            webHostManager.Setup(p => p.GetChannels("node")).Returns(new Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>>
            {
                ["webhost"] = completed,
                ["pending"] = pending
            });

            var dispatcherFactory = new Mock<IFunctionInvocationDispatcherFactory>(MockBehavior.Strict);
            dispatcherFactory.Setup(p => p.GetFunctionDispatcher()).Returns(Mock.Of<IFunctionInvocationDispatcher>());

            var manager = new RpcScriptHostWorkerManager(dispatcherFactory.Object, jobHostManager.Object, webHostManager.Object);

            IReadOnlyList<WorkerRuntimeInfo> result = manager.GetWorkerRuntimeInfo("node");

            Assert.Equal([nodeInfo, completedWebHostChannel.ReportedRuntimeInfo], result);
        }

        [Fact]
        public void GetWorkerRuntimeInfo_IncludesJobHostChannelsForAllLanguages()
        {
            var nodeInfo = new WorkerRuntimeInfo("node", "22.11.0", "3.10.1");
            var pythonInfo = new WorkerRuntimeInfo("python", "3.12.0", "4.0.0");
            var jobHostManager = new JobHostRpcWorkerChannelManager(MockNullLoggerFactory.CreateLoggerFactory());
            jobHostManager.AddChannel(new TestRpcWorkerChannel("node") { ReportedRuntimeInfo = nodeInfo }, "node");
            jobHostManager.AddChannel(new TestRpcWorkerChannel("python") { ReportedRuntimeInfo = pythonInfo }, "python");

            var webHostManager = new Mock<IWebHostRpcWorkerChannelManager>(MockBehavior.Strict);
            webHostManager.Setup(p => p.GetChannels("dotnet")).Returns((IDictionary<string, TaskCompletionSource<IRpcWorkerChannel>>)null);
            var dispatcherFactory = new Mock<IFunctionInvocationDispatcherFactory>(MockBehavior.Strict);
            dispatcherFactory.Setup(p => p.GetFunctionDispatcher()).Returns(Mock.Of<IFunctionInvocationDispatcher>());

            var manager = new RpcScriptHostWorkerManager(dispatcherFactory.Object, jobHostManager, webHostManager.Object);
            IReadOnlyList<WorkerRuntimeInfo> result = manager.GetWorkerRuntimeInfo("dotnet");

            Assert.Equal(2, result.Count);
            Assert.Contains(nodeInfo, result);
            Assert.Contains(pythonInfo, result);
        }

        [Fact]
        public void GetWorkerRuntimeInfo_DefaultImplementationReturnsNoReports()
        {
            IScriptHostWorkerManager manager = new ExistingWorkerManager();

            Assert.Empty(manager.GetWorkerRuntimeInfo("node"));
        }

        private sealed class ExistingWorkerManager : IScriptHostWorkerManager
        {
            public WorkerManagerState State => throw new NotSupportedException();

            public Task GetWorkerStatusesAsync() => throw new NotSupportedException();

            public Task<bool> RestartWorkerWithInvocationIdAsync(string invocationId, Exception exception) => throw new NotSupportedException();

            public Task<IEnumerable<WorkerProcessInfo>> GetWorkerProcessInfoAsync(string workerRuntime) => throw new NotSupportedException();
        }
    }
}
