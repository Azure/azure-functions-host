// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Executors
{
    public class TriggeredFunctionExecutorTests
    {
        [Fact]
        public async Task TryExecuteAsync_WithInvokeHandler_InvokesHandler()
        {
            var mockExecutor = new Mock<IFunctionExecutor>();
            mockExecutor.Setup(m => m.TryExecuteAsync(It.IsAny<IFunctionInstance>(), It.IsAny<CancellationToken>())).
                Returns<IFunctionInstance, CancellationToken>((x, y) =>
                {
                    x.Invoker.InvokeAsync(null, null).Wait();
                    return Task.FromResult<IDelayedException>(null);
                });

            bool innerInvokerInvoked = false;
            Mock<IFunctionInvoker> mockInvoker = new Mock<IFunctionInvoker>();
            mockInvoker.Setup(m => m.InvokeAsync(null, null)).Returns(() =>
            {
                innerInvokerInvoked = true;
                return Task.FromResult<object>(null);
            });

            bool customInvokerInvoked = false;
            Func<Func<Task>, Task> invokeHandler = async (inner) =>
            {
                customInvokerInvoked = true;
                await inner();
            };

            var mockTriggerBinding = new Mock<ITriggeredFunctionBinding<int>>();
            var functionDescriptor = new FunctionDescriptor();
            var instanceFactory = new TriggeredFunctionInstanceFactory<int>(mockTriggerBinding.Object, mockInvoker.Object, functionDescriptor);
            var triggerExecutor = new TriggeredFunctionExecutor<int>(functionDescriptor, mockExecutor.Object, instanceFactory);

            // specify a custom handler on the trigger data and
            // verify it is invoked when the trigger executes
            var triggerData = new TriggeredFunctionData
            {
                TriggerValue = 123,
                InvokeHandler = invokeHandler
            };

            var result = await triggerExecutor.TryExecuteAsync(triggerData, CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.True(customInvokerInvoked);
            Assert.True(innerInvokerInvoked);
        }
    }
}