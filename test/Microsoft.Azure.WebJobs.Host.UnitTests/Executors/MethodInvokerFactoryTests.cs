// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;
using Xunit.Extensions;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Executors
{
    public class MethodInvokerFactoryTests
    {
        private static bool _parameterlessMethodCalled;

        [Fact]
        public void Create_IfMethodIsNull_Throws()
        {
            // Arrange
            MethodInfo method = null;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(() => MethodInvokerFactory.Create<MethodInvokerFactoryTests>(method),
                "method");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Create_IfStaticMethodReturnsVoid_ReturnsVoidInvoker(bool isInstance)
        {
            // Arrange
            MethodInfo method = GetMethodInfo(isInstance, "ReturnVoid");

            // Act
            IMethodInvoker<MethodInvokerFactoryTests> invoker =
                MethodInvokerFactory.Create<MethodInvokerFactoryTests>(method);

            // Assert
            Assert.IsType<VoidMethodInvoker<MethodInvokerFactoryTests>>(invoker);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Create_IfStaticMethodReturnsTask_ReturnsTaskInvoker(bool isInstance)
        {
            // Arrange
            MethodInfo method = GetMethodInfo(isInstance, "ReturnTask");

            // Act
            IMethodInvoker<MethodInvokerFactoryTests> invoker =
                MethodInvokerFactory.Create<MethodInvokerFactoryTests>(method);

            // Assert
            Assert.IsType<TaskMethodInvoker<MethodInvokerFactoryTests>>(invoker);
        }

        [Fact]
        public void Create_IfMethodReturnsNonTask_Throws()
        {
            // Arrange
            MethodInfo method = GetMethodInfo("ReturnInt");

            // Act & Assert
            ExceptionAssert.ThrowsNotSupported(() => MethodInvokerFactory.Create<MethodInvokerFactoryTests>(method),
                "Methods may only return void or Task.");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Create_IfTReflectedIsNotReflectedType_Throws(bool isInstance)
        {
            // Arrange
            MethodInfo method = GetMethodInfo(isInstance, "ReturnVoid");

            // Act & Assert
            ExceptionAssert.ThrowsInvalidOperation(() => MethodInvokerFactory.Create<object>(method),
                "TReflected must match the method's ReflectedType.");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Create_IfMultipleInputParameters_PassesInputArguments(bool isInstance)
        {
            // Arrange
            MethodInfo method = GetMethodInfo(isInstance, "TestIntStringObjectArray");
            int expectedA = 1;
            string expectedB = "B";
            object[] expectedC = new object[] { new object() };

            // Act
            IMethodInvoker<MethodInvokerFactoryTests> invoker =
                MethodInvokerFactory.Create<MethodInvokerFactoryTests>(method);

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
            MethodInvokerFactoryTests instance = GetInstance(isInstance);
            object[] arguments = new object[] { expectedA, expectedB, expectedC, callback };
            invoker.InvokeAsync(instance, arguments).GetAwaiter().GetResult();
            Assert.True(callbackCalled);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Create_IfMultipleOutputParameters_SetsOutputArguments(bool isInstance)
        {
            // Arrange
            MethodInfo method = GetMethodInfo(isInstance, "TestOutIntStringObjectArray");
            int expectedA = 1;
            string expectedB = "B";
            object[] expectedC = new object[] { new object() };

            // Act
            IMethodInvoker<MethodInvokerFactoryTests> invoker =
                MethodInvokerFactory.Create<MethodInvokerFactoryTests>(method);

            // Assert
            Assert.NotNull(invoker);
            bool callbackCalled = false;
            OutAction callback = delegate(out int a, out string b, out object[] c)
            {
                callbackCalled = true;
                a = expectedA;
                b = expectedB;
                c = expectedC;
            };
            MethodInvokerFactoryTests instance = GetInstance(isInstance);
            object[] arguments = new object[] { default(int), null, null, callback };
            invoker.InvokeAsync(instance, arguments).GetAwaiter().GetResult();
            Assert.True(callbackCalled);
            Assert.Equal(expectedA, arguments[0]);
            Assert.Same(expectedB, arguments[1]);
            Assert.Same(expectedC, arguments[2]);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Create_IfMultipleReferenceParameters_RoundtripsArguments(bool isInstance)
        {
            // Arrange
            MethodInfo method = GetMethodInfo(isInstance, "TestByRefIntStringObjectArray");
            int expectedInitialA = 1;
            string expectedInitialB = "B";
            object[] expectedInitialC = new object[] { new object() };
            int expectedFinalA = 2;
            string expectedFinalB = "b";
            object[] expectedFinalC = new object[] { new object(), default(int), String.Empty };

            // Act
            IMethodInvoker<MethodInvokerFactoryTests> invoker =
                MethodInvokerFactory.Create<MethodInvokerFactoryTests>(method);

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
            MethodInvokerFactoryTests instance = GetInstance(isInstance);
            object[] arguments = new object[] { expectedInitialA, expectedInitialB, expectedInitialC, callback };
            invoker.InvokeAsync(instance, arguments).GetAwaiter().GetResult();
            Assert.True(callbackCalled);
            Assert.Equal(expectedFinalA, arguments[0]);
            Assert.Same(expectedFinalB, arguments[1]);
            Assert.Same(expectedFinalC, arguments[2]);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Create_IfInOutByRefMethodReturnsTask_RoundtripsArguments(bool isInstance)
        {
            // Arrange
            MethodInfo method = GetMethodInfo(isInstance, "TestInOutByRefReturnTask");
            int expectedA = 1;
            string expectedInitialB = "B";
            string expectedFinalB = "b";
            object[] expectedC = new object[] { new object(), default(int), String.Empty };

            // Act
            IMethodInvoker<MethodInvokerFactoryTests> invoker =
                MethodInvokerFactory.Create<MethodInvokerFactoryTests>(method);

            // Assert
            Assert.NotNull(invoker);
            bool callbackCalled = false;
            InOutRefTaskFunc callback = delegate(int a, ref string b, out object[] c)
            {
                callbackCalled = true;
                Assert.Equal(expectedA, a);
                Assert.Same(expectedInitialB, b);
                b = expectedFinalB;
                c = expectedC;
                return Task.FromResult(0);
            };
            MethodInvokerFactoryTests instance = GetInstance(isInstance);
            object[] arguments = new object[] { expectedA, expectedInitialB, null, callback };
            invoker.InvokeAsync(instance, arguments).GetAwaiter().GetResult();
            Assert.True(callbackCalled);
            Assert.Same(expectedFinalB, arguments[1]);
            Assert.Same(expectedC, arguments[2]);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Create_IfReturnsTaskAndTaskCanceled_ReturnsCanceledTask(bool isInstance)
        {
            // Arrange
            MethodInfo method = GetMethodInfo(isInstance, "ReturnCanceledTask");

            // Act
            IMethodInvoker<MethodInvokerFactoryTests> invoker =
                MethodInvokerFactory.Create<MethodInvokerFactoryTests>(method);

            // Assert
            MethodInvokerFactoryTests instance = GetInstance(isInstance);
            Task task = invoker.InvokeAsync(instance, null);
            Assert.NotNull(task);
            task.WaitUntilCompleted();
            Assert.Equal(TaskStatus.Canceled, task.Status);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Create_IfParameterlessMethod_CanInvoke(bool isInstance)
        {
            // Arrange
            MethodInfo method = GetMethodInfo(isInstance, "ParameterlessMethod");

            // Act
            IMethodInvoker<MethodInvokerFactoryTests> invoker =
                MethodInvokerFactory.Create<MethodInvokerFactoryTests>(method);

            try
            {
                // Assert
                MethodInvokerFactoryTests instance = GetInstance(isInstance);
                invoker.InvokeAsync(instance, null).GetAwaiter().GetResult();
                Assert.True(_parameterlessMethodCalled);
            }
            finally
            {
                _parameterlessMethodCalled = false;
            }
        }

        private MethodInvokerFactoryTests GetInstance(bool isInstance)
        {
            return isInstance ? this : null;
        }

        private static MethodInfo GetMethodInfo(bool isInstance, string name)
        {
            return GetMethodInfo(GetPrefixedMethodName(isInstance, name));
        }

        private static MethodInfo GetMethodInfo(string name)
        {
            return typeof(MethodInvokerFactoryTests).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static |
                BindingFlags.Instance);
        }

        private static string GetPrefixedMethodName(bool isInstance, string name)
        {
            string prefix = isInstance ? "Instance" : "Static";
            return prefix + name;
        }

        private static int ReturnInt() { return default(int); }

        private static void StaticReturnVoid() { }

        private static Task StaticReturnTask() { return null; }

        private void InstanceReturnVoid() { }

        private Task InstanceReturnTask() { return null; }

        private static void StaticTestIntStringObjectArray(int a, string b, object[] c,
            Action<int, string, object[]> callback)
        {
            callback.Invoke(a, b, c);
        }

        private void InstanceTestIntStringObjectArray(int a, string b, object[] c,
            Action<int, string, object[]> callback)
        {
            callback.Invoke(a, b, c);
        }

        private delegate void OutAction(out int a, out string b, out object[] c);

        private static void StaticTestOutIntStringObjectArray(out int a, out string b, out object[] c,
            OutAction callback)
        {
            callback.Invoke(out a, out b, out c);
        }

        private void InstanceTestOutIntStringObjectArray(out int a, out string b, out object[] c, OutAction callback)
        {
            callback.Invoke(out a, out b, out c);
        }

        private delegate void ByRefAction(ref int a, ref string b, ref object[] c);

        private static void StaticTestByRefIntStringObjectArray(ref int a, ref string b, ref object[] c,
            ByRefAction callback)
        {
            callback.Invoke(ref a, ref b, ref c);
        }

        private void InstanceTestByRefIntStringObjectArray(ref int a, ref string b, ref object[] c,
            ByRefAction callback)
        {
            callback.Invoke(ref a, ref b, ref c);
        }

        private static Task StaticReturnCanceledTask()
        {
            TaskCompletionSource<object> source = new TaskCompletionSource<object>();
            source.SetCanceled();
            return source.Task;
        }

        private Task InstanceReturnCanceledTask()
        {
            TaskCompletionSource<object> source = new TaskCompletionSource<object>();
            source.SetCanceled();
            return source.Task;
        }

        private static void StaticParameterlessMethod()
        {
            _parameterlessMethodCalled = true;
        }

        private void InstanceParameterlessMethod()
        {
            _parameterlessMethodCalled = true;
        }

        private delegate Task InOutRefTaskFunc(int a, ref string b, out object[] c);

        private static Task StaticTestInOutByRefReturnTask(int a, ref string b, out object[] c,
            InOutRefTaskFunc callback)
        {
            return callback.Invoke(a, ref b, out c);
        }

        private Task InstanceTestInOutByRefReturnTask(int a, ref string b, out object[] c, InOutRefTaskFunc callback)
        {
            return callback.Invoke(a, ref b, out c);
        }
    }
}
