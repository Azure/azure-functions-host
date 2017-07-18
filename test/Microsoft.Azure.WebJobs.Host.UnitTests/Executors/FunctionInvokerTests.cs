// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Moq;
using Xunit;

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
            product.InvokeAsync(expectedArguments).GetAwaiter().GetResult();
            
            // Assert
            instanceFactoryMock.VerifyAll();
            methodInvokerMock.VerifyAll();
        }

        [Fact]
        public void InvokeAsync_IfInstanceIsDisposable_Disposes()
        {
            // Arrange
            bool disposed = false;

            Mock<IDisposable> disposableMock = new Mock<IDisposable>(MockBehavior.Strict);
            disposableMock.Setup(d => d.Dispose()).Callback(() => { disposed = true; });
            IDisposable disposable = disposableMock.Object;

            IFactory<object> instanceFactory = CreateStubFactory(disposable);
            IMethodInvoker<object, object> methodInvoker = CreateStubMethodInvoker();

            IFunctionInvoker product = CreateProductUnderTest(instanceFactory, methodInvoker);
            object[] arguments = new object[0];

            // Act
            product.InvokeAsync(arguments).GetAwaiter().GetResult();

            // Assert
            Assert.True(disposed);
        }

        [Fact]
        public void InvokeAsync_IfInstanceIsDisposable_DoesNotDisposeWhileTaskIsRunning()
        {
            // Arrange
            bool disposed = false;

            Mock<IDisposable> disposableMock = new Mock<IDisposable>(MockBehavior.Strict);
            disposableMock.Setup(d => d.Dispose()).Callback(() => { disposed = true; });
            IDisposable disposable = disposableMock.Object;

            IFactory<object> instanceFactory = CreateStubFactory(disposable);
            TaskCompletionSource<object> taskSource = new TaskCompletionSource<object>();
            IMethodInvoker<object, object> methodInvoker = CreateStubMethodInvoker(taskSource.Task);

            IFunctionInvoker product = CreateProductUnderTest(instanceFactory, methodInvoker);
            object[] arguments = new object[0];

            // Act
            Task task = product.InvokeAsync(arguments);

            // Assert
            Assert.NotNull(task);
            Assert.False(disposed);
            taskSource.SetResult(null);
            task.GetAwaiter().GetResult();
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

        private static IFactory<object> CreateStubFactory(object instance)
        {
            Mock<IFactory<object>> mock = new Mock<IFactory<object>>(MockBehavior.Strict);
            mock.Setup(f => f.Create())
                .Returns(instance);
            return mock.Object;
        }

        private static IMethodInvoker<object, object> CreateStubMethodInvoker()
        {
            return CreateStubMethodInvoker(Task.FromResult<object>(null));
        }

        private static IMethodInvoker<object, object> CreateStubMethodInvoker(Task<object> task)
        {
            Mock<IMethodInvoker<object, object>> mock = new Mock<IMethodInvoker<object, object>>(MockBehavior.Strict);
            mock.Setup(i => i.InvokeAsync(It.IsAny<object>(), It.IsAny<object[]>()))
                .Returns(task);
            return mock.Object;
        }
    }
}
