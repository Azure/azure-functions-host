// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionDispatcherTerminatorTests
    {
        [Fact]
        public async Task Test_StopAsync()
        {
            Mock<IFunctionDispatcher> functionDispatcher = new Mock<IFunctionDispatcher>();
            Mock<IFunctionDispatcher> functionDispatcher2 = new Mock<IFunctionDispatcher>();
            functionDispatcher.Setup(a => a.TerminateAsync()).Returns(Task.CompletedTask);
            functionDispatcher2.Setup(a => a.TerminateAsync()).Returns(Task.CompletedTask);
            var functionDispatcherTerminator = new FunctionDispatcherTerminator(new List<IFunctionDispatcher> { functionDispatcher.Object, functionDispatcher2.Object });
            await functionDispatcherTerminator.StopAsync(CancellationToken.None);
            functionDispatcher.Verify(a => a.TerminateAsync(), Times.Once);
            functionDispatcher2.Verify(a => a.TerminateAsync(), Times.Once);
        }
    }
}
