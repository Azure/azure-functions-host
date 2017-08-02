// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Moq;
using Xunit;
using Microsoft.Azure.WebJobs.Host.TestCommon;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Executors
{
    public class FunctionInvokerTests
    {
        [Fact]
        public void InvokeAsync_DelegatesToInstanceFactoryAndMethodInvoker()
        {
            // Arrange
            object expectedInstance = new object();
            object[] expectedArguments = new object[0];

            Mock<IFactory<object>> instanceFactoryMock = new Mock<IFactory<object>>(MockBehavior.Strict);
            instanceFactoryMock.Setup(f => f.Create())
                               .Returns(expectedInstance)
                               .Verifiable();
            IFactory<object> instanceFactory = instanceFactoryMock.Object;

            Mock<IMethodInvoker<object, object>> methodInvokerMock = new Mock<IMethodInvoker<object, object>>(MockBehavior.Strict);
            methodInvokerMock.Setup(i => i.InvokeAsync(expectedInstance, expectedArguments))
                             .Returns(Task.FromResult<object>(null))
                             .Verifiable();
            IMethodInvoker<object, object> methodInvoker = methodInvokerMock.Object;

            IFunctionInvoker product = CreateProductUnderTest(instanceFactory, methodInvoker);

            // Act
            var instance = product.CreateInstance();
            product.InvokeAsync(instance, expectedArguments).GetAwaiter().GetResult();
            
            // Assert
            instanceFactoryMock.VerifyAll();
            methodInvokerMock.VerifyAll();
        }

        // Simple program to test IDisposable.
        public class MyProg : IDisposable
        {
            public bool _disposed;

            public Task _waiter = Task.CompletedTask;

            public void Dispose()
            {
                _disposed = true;
            }

            [NoAutomaticTrigger]
            public async Task Method()
            {
                // Allow this method to pause while the test verifies that Dispose is not yet called. 
                await _waiter;
            }
        }

        [Fact]
        public void InvokeAsync_IfInstanceIsDisposable_DoesNotDisposeWhileTaskIsRunning2()
        {
            var prog = new MyProg();
            var tsc = new TaskCompletionSource<object>();
            prog._waiter = tsc.Task;
            var activator = new FakeActivator(prog);
            var host = TestHelpers.NewJobHost<MyProg>(activator);

            var task = host.CallAsync("MyProg.Method");
            Assert.True(!task.IsCompleted);

            Assert.False(prog._disposed, "User job should not yet be disposed.");
            tsc.SetResult(true); // This will let method run to completion and call dispose. 

            task.Wait(3000);
            Assert.True(task.IsCompleted);
            Assert.True(prog._disposed, "User job should be disposed.");
        }

        private static FunctionInvoker<object, object> CreateProductUnderTest(IFactory<object> instanceFactory,
            IMethodInvoker<object, object> methodInvoker)
        {
            return CreateProductUnderTest<object, object>(new string[0], instanceFactory, methodInvoker);
        }

        private static FunctionInvoker<TReflected, TReturnValue> CreateProductUnderTest<TReflected, TReturnValue>(
            IReadOnlyList<string> parameterNames,
            IFactory<TReflected> instanceFactory,
            IMethodInvoker<TReflected, TReturnValue> methodInvoker)
        {
            return new FunctionInvoker<TReflected, TReturnValue>(parameterNames, instanceFactory, methodInvoker);
        }
    }
}
