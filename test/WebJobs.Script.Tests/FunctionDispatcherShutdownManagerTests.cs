// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionDispatcherShutdownManagerTests
    {
        [Fact]
        public async Task Test_StopAsync()
        {
            Mock<IFunctionDispatcher> functionDispatcher = new Mock<IFunctionDispatcher>();
            functionDispatcher.Setup(a => a.ShutdownAsync()).Returns(Task.CompletedTask);
            var functionDispatcherShutdownManager = new FunctionDispatcherShutdownManager(functionDispatcher.Object);
            await functionDispatcherShutdownManager.StopAsync(CancellationToken.None);
            functionDispatcher.Verify(a => a.ShutdownAsync(), Times.Once);
        }
    }
}
