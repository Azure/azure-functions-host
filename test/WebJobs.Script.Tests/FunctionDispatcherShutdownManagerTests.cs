// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.OutOfProc;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionDispatcherShutdownManagerTests
    {
        [Fact]
        public async Task FunctionDispatcherShutdownManager_ShutdownAsync_Succeeds()
        {
            Mock<IFunctionDispatcher> mockFunctionDispatcher = new Mock<IFunctionDispatcher>();
            mockFunctionDispatcher.Setup(a => a.ShutdownAsync()).Returns(Task.CompletedTask);

            Mock<IFunctionDispatcherFactory> mockFunctionDispatcherFactory = new Mock<IFunctionDispatcherFactory>();
            mockFunctionDispatcherFactory.Setup(functionDispatcherFactory => functionDispatcherFactory.GetFunctionDispatcher()).Returns(mockFunctionDispatcher.Object);

            var functionDispatcherShutdownManager = new FunctionDispatcherShutdownManager(mockFunctionDispatcherFactory.Object);
            await functionDispatcherShutdownManager.StopAsync(CancellationToken.None);
            mockFunctionDispatcher.Verify(functionDispatcher => functionDispatcher.ShutdownAsync(), Times.Once);
        }
    }
}
