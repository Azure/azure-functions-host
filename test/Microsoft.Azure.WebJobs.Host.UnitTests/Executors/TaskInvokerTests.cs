// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Executors
{
    public class TaskInvokerTests
    {
        [Fact]
        public void InvokeAsync_DelegatesToLambda()
        {
            // Arrange
            object[] expectedArguments = new object[0];
            bool invoked = false;
            object[] arguments = null;
            Func<object[], Task> lambda = (a) =>
            {
                invoked = true;
                arguments = a;
                return Task.FromResult(0);
            };

            IInvoker invoker = CreateProductUnderTest(lambda);

            // Act
            Task task = invoker.InvokeAsync(expectedArguments);
            
            // Assert
            Assert.NotNull(task);
            task.GetAwaiter().GetResult();
            Assert.True(invoked);
            Assert.Same(expectedArguments, arguments);
        }

        [Fact]
        public void InvokeAsync_IfLambdaThrows_PropogatesException()
        {
            // Arrange
            InvalidOperationException expectedException = new InvalidOperationException();
            Func<object[], Task> lambda = (_) =>
            {
                throw expectedException;
            };

            IInvoker invoker = CreateProductUnderTest(lambda);
            object[] arguments = null;

            // Act & Assert
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => invoker.InvokeAsync(arguments));
            Assert.Same(expectedException, exception);
        }

        [Fact]
        public void InvokeAsync_IfLambdaReturnsCanceledTask_ReturnsCanceledTask()
        {
            // Arrange
            Func<object[], Task> lambda = (_) =>
            {
                TaskCompletionSource<object> source = new TaskCompletionSource<object>();
                source.SetCanceled();
                return source.Task;
            };

            IInvoker invoker = CreateProductUnderTest(lambda);
            object[] arguments = null;

            // Act
            Task task = invoker.InvokeAsync(arguments);

            // Assert
            Assert.NotNull(task);
            task.WaitUntilCompleted();
            Assert.Equal(TaskStatus.Canceled, task.Status);
        }

        [Fact]
        public void InvokeAsync_IfLambdaReturnsFaultedTask_ReturnsFaultedTask()
        {
            // Arrange
            Exception expectedException = new InvalidOperationException();
            Func<object[], Task> lambda = (_) =>
            {
                TaskCompletionSource<object> source = new TaskCompletionSource<object>();
                source.SetException(expectedException);
                return source.Task;
            };

            IInvoker invoker = CreateProductUnderTest(lambda);
            object[] arguments = null;

            // Act
            Task task = invoker.InvokeAsync(arguments);

            // Assert
            Assert.NotNull(task);
            task.WaitUntilCompleted();
            Assert.Equal(TaskStatus.Faulted, task.Status);
            Assert.NotNull(task.Exception);
            Assert.Same(expectedException, task.Exception.InnerException);
        }

        [Fact]
        public void InvokeAsync_IfLambdaReturnsNull_ReturnsNull()
        {
            // Arrange
            Func<object[], Task> lambda = (_) => null;
            IInvoker invoker = CreateProductUnderTest(lambda);
            object[] arguments = null;

            // Act
            Task task = invoker.InvokeAsync(arguments);

            // Assert
            Assert.Null(task);
        }

        [Fact]
        public void InvokeAsync_IfLambdaReturnsNonGenericTask_ReturnsCompletedTask()
        {
            // Arrange
            Func<object[], Task> lambda = (_) =>
            {
                Task innerTask = new Task(() => { });
                innerTask.Start();
                Assert.False(innerTask.GetType().IsGenericType); // Guard
                return innerTask;
            };

            IInvoker invoker = CreateProductUnderTest(lambda);
            object[] arguments = null;

            // Act
            Task task = invoker.InvokeAsync(arguments);

            // Assert
            Assert.NotNull(task);
            task.WaitUntilCompleted();
            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
        }

        [Fact]
        public void InvokeAsync_IfLambdaReturnsNestedTask_Throws()
        {
            // Arrange
            Func<object[], Task> lambda = (_) =>
            {
                TaskCompletionSource<Task> source = new TaskCompletionSource<Task>();
                source.SetResult(null);
                return source.Task;
            };

            IInvoker invoker = CreateProductUnderTest(lambda);
            object[] arguments = null;

            // Act & Assert
            ExceptionAssert.ThrowsInvalidOperation(() => invoker.InvokeAsync(arguments), "Returning a nested Task is " +
                "not supported. Did you mean to await or Unwrap the task instead of returning it?");
        }

        private static TaskInvoker CreateProductUnderTest(Func<object[], Task> lambda)
        {
            return new TaskInvoker(new List<string>(), lambda);
        }
    }
}
