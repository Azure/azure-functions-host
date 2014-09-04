// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Executors
{
    public class InvokerFactoryTests
    {
        private static bool _parameterlessMethodCalled;

        [Fact]
        public void Create_IfMethodIsNull_Throws()
        {
            // Arrange
            MethodInfo method = null;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(() => InvokerFactory.Create(method), "method");
        }

        [Fact]
        public void Create_IfMethodIsInstance_Throws()
        {
            // Arrange
            MethodInfo method = typeof(InvokerFactoryTests).GetMethod("InstanceMethod",
                BindingFlags.NonPublic | BindingFlags.Instance);

            // Act & Assert
            ExceptionAssert.ThrowsNotSupported(() => InvokerFactory.Create(method),
                "Only static methods can be invoked.");
        }

        [Fact]
        public void Create_IfMethodReturnsVoid_ReturnsVoidInvoker()
        {
            // Arrange
            MethodInfo method = GetMethodInfo("ReturnVoid");

            // Act
            IInvoker invoker = InvokerFactory.Create(method);

            // Assert
            Assert.IsType<VoidInvoker>(invoker);
        }

        [Fact]
        public void Create_IfMethodReturnsTask_ReturnsTaskInvoker()
        {
            // Arrange
            MethodInfo method = GetMethodInfo("ReturnTask");

            // Act
            IInvoker invoker = InvokerFactory.Create(method);

            // Assert
            Assert.IsType<TaskInvoker>(invoker);
        }

        [Fact]
        public void Create_IfMethodReturnsNonTask_Throws()
        {
            // Arrange
            MethodInfo method = GetMethodInfo("ReturnInt");

            // Act & Assert
            ExceptionAssert.ThrowsNotSupported(() => InvokerFactory.Create(method),
                "Methods may only return void or Task.");
        }

        [Fact]
        public void Create_IfNoParameters_ReturnsEmptyParameterNames()
        {
            // Arrange
            MethodInfo method = GetMethodInfo("NoParameters");

            // Act
            IInvoker invoker = InvokerFactory.Create(method);

            // Assert
            Assert.NotNull(invoker);
            Assert.Equal(Enumerable.Empty<string>(), invoker.ParameterNames);
        }

        [Fact]
        public void Create_IfMultipleParameters_ReturnsParameterNames()
        {
            // Arrange
            MethodInfo method = GetMethodInfo("ParametersFooBarBaz");

            // Act
            IInvoker invoker = InvokerFactory.Create(method);

            // Assert
            Assert.NotNull(invoker);
            Assert.Equal((IEnumerable<string>)new string[] { "foo", "bar", "baz" }, invoker.ParameterNames);
        }

        [Fact]
        public void Create_IfMultipleInputParameters_PassesInputArguments()
        {
            // Arrange
            MethodInfo method = GetMethodInfo("TestIntStringObjectArray");
            int expectedA = 1;
            string expectedB = "B";
            object[] expectedC = new object[] {new object()};

            // Act
            IInvoker invoker = InvokerFactory.Create(method);

            // Assert
            Assert.NotNull(invoker);
            bool callbackCalled = false;
            Action<int, string, object> callback = (a, b, c) =>
                {
                    callbackCalled = true;
                    Assert.Equal(expectedA, a);
                    Assert.Same(expectedB, b);
                    Assert.Same(expectedC, c);
                };
            object[] arguments = new object[] { expectedA, expectedB, expectedC, callback };
            invoker.InvokeAsync(arguments).GetAwaiter().GetResult();
            Assert.True(callbackCalled);
        }

        [Fact]
        public void Create_IfMultipleOutputParameters_SetsOutputArguments()
        {
            // Arrange
            MethodInfo method = GetMethodInfo("TestOutIntStringObjectArray");
            int expectedA = 1;
            string expectedB = "B";
            object[] expectedC = new object[] { new object() };

            // Act
            IInvoker invoker = InvokerFactory.Create(method);

            // Assert
            Assert.NotNull(invoker);
            bool callbackCalled = false;
            OutAction callback = delegate (out int a, out string b, out object[] c)
            {
                callbackCalled = true;
                a = expectedA;
                b = expectedB;
                c = expectedC;
            };
            object[] arguments = new object[] { default(int), null, null, callback };
            invoker.InvokeAsync(arguments).GetAwaiter().GetResult();
            Assert.True(callbackCalled);
            Assert.Equal(expectedA, arguments[0]);
            Assert.Same(expectedB, arguments[1]);
            Assert.Same(expectedC, arguments[2]);
        }

        [Fact]
        public void Create_IfMultipleReferenceParameters_RoundtripsArguments()
        {
            // Arrange
            MethodInfo method = GetMethodInfo("TestByRefIntStringObjectArray");
            int expectedInitialA = 1;
            string expectedInitialB = "B";
            object[] expectedInitialC = new object[] { new object() };
            int expectedFinalA = 2;
            string expectedFinalB = "b";
            object[] expectedFinalC = new object[] { new object(), default(int), String.Empty };

            // Act
            IInvoker invoker = InvokerFactory.Create(method);

            // Assert
            Assert.NotNull(invoker);
            bool callbackCalled = false;
            ByRefAction callback = delegate(ref int a, ref string b, ref object[] c)
            {
                callbackCalled = true;
                Assert.Equal(expectedInitialA, a);
                Assert.Same(expectedInitialB, b);
                Assert.Same(expectedInitialC, c);
                a = expectedFinalA;
                b = expectedFinalB;
                c = expectedFinalC;
            };
            object[] arguments = new object[] { expectedInitialA, expectedInitialB, expectedInitialC, callback };
            invoker.InvokeAsync(arguments).GetAwaiter().GetResult();
            Assert.True(callbackCalled);
            Assert.Equal(expectedFinalA, arguments[0]);
            Assert.Same(expectedFinalB, arguments[1]);
            Assert.Same(expectedFinalC, arguments[2]);
        }

        [Fact]
        public void Create_IfReturnsTaskAndTaskCanceled_ReturnsCanceledTask()
        {
            // Arrange
            MethodInfo method = GetMethodInfo("ReturnCanceledTask");

            // Act
            IInvoker invoker = InvokerFactory.Create(method);

            // Assert
            Task task = invoker.InvokeAsync(null);
            Assert.NotNull(task);
            task.WaitUntilCompleted();
            Assert.Equal(TaskStatus.Canceled, task.Status);
        }

        [Fact]
        public void Create_IfParameterlessMethod_CanInvoke()
        {
            // Arrange
            MethodInfo method = GetMethodInfo("ParameterlessMethod");

            // Act
            IInvoker invoker = InvokerFactory.Create(method);

            try
            {
                // Assert
                invoker.InvokeAsync(null).GetAwaiter().GetResult();
                Assert.True(_parameterlessMethodCalled);
            }
            finally
            {
                _parameterlessMethodCalled = false;
            }
        }

        private static MethodInfo GetMethodInfo(string name)
        {
            return typeof(InvokerFactoryTests).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
        }

        private void InstanceMethod() { }

        private static int ReturnInt() { return default(int); }

        private static void ReturnVoid() { }

        private static Task ReturnTask() { return null; }

        private static void NoParameters() { }

        private static void ParametersFooBarBaz(int foo, string bar, object baz) { }

        private static void TestIntStringObjectArray(int a, string b, object[] c,
            Action<int, string, object[]> callback)
        {
            callback.Invoke(a, b, c);
        }

        private delegate void OutAction(out int a, out string b, out object[] c);

        private static void TestOutIntStringObjectArray(out int a, out string b, out object[] c, OutAction callback)
        {
            callback.Invoke(out a, out b, out c);
        }

        private delegate void ByRefAction(ref int a, ref string b, ref object[] c);

        private static void TestByRefIntStringObjectArray(ref int a, ref string b, ref object[] c, ByRefAction callback)
        {
            callback.Invoke(ref a, ref b, ref c);
        }

        private static Task ReturnCanceledTask()
        {
            TaskCompletionSource<object> source = new TaskCompletionSource<object>();
            source.SetCanceled();
            return source.Task;
        }

        private static void ParameterlessMethod()
        {
            _parameterlessMethodCalled = true;
        }
    }
}
