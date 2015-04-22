// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.TestCommon;
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

            Mock<IMethodInvoker<object>> methodInvokerMock = new Mock<IMethodInvoker<object>>(MockBehavior.Strict);
            methodInvokerMock.Setup(i => i.InvokeAsync(expectedInstance, expectedArguments))
                             .Returns(Task.FromResult(0))
                             .Verifiable();
            IMethodInvoker<object> methodInvoker = methodInvokerMock.Object;

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
            IMethodInvoker<object> methodInvoker = CreateStubMethodInvoker();

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
            IMethodInvoker<object> methodInvoker = CreateStubMethodInvoker(taskSource.Task);

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

        private static FunctionInvoker<object> CreateProductUnderTest(IFactory<object> instanceFactory,
            IMethodInvoker<object> methodInvoker)
        {
            return CreateProductUnderTest<object>(new string[0], instanceFactory, methodInvoker);
        }

        private static FunctionInvoker<TReflected> CreateProductUnderTest<TReflected>(
            IReadOnlyList<string> parameterNames,
            IFactory<TReflected> instanceFactory,
            IMethodInvoker<TReflected> methodInvoker)
        {
            return new FunctionInvoker<TReflected>(parameterNames, instanceFactory, methodInvoker);
        }

        private static IFactory<object> CreateStubFactory(object instance)
        {
            Mock<IFactory<object>> mock = new Mock<IFactory<object>>(MockBehavior.Strict);
            mock.Setup(f => f.Create())
                .Returns(instance);
            return mock.Object;
        }

        private static IMethodInvoker<object> CreateStubMethodInvoker()
        {
            return CreateStubMethodInvoker(Task.FromResult(0));
        }

        private static IMethodInvoker<object> CreateStubMethodInvoker(Task task)
        {
            Mock<IMethodInvoker<object>> mock = new Mock<IMethodInvoker<object>>(MockBehavior.Strict);
            mock.Setup(i => i.InvokeAsync(It.IsAny<object>(), It.IsAny<object[]>()))
                .Returns(task);
            return mock.Object;
        }
    }
}
