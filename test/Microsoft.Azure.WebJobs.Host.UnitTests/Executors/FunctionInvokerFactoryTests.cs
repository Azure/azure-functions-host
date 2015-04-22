// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Executors
{
    public class FunctionInvokerFactoryTests
    {
        [Fact]
        public void Create_IfMethodIsNull_Throws()
        {
            // Arrange
            MethodInfo method = null;
            IJobActivator activator = CreateDummyActivator();

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(() => FunctionInvokerFactory.Create(method, activator), "method");
        }

        [Fact]
        public void Create_IfActivatorIsNull_Throws()
        {
            // Arrange
            MethodInfo method = GetMethodInfo("StaticReturnVoid");
            IJobActivator activator = null;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(() => FunctionInvokerFactory.Create(method, activator), "activator");
        }

        [Fact]
        public void Create_ReturnsFunctionInvoker()
        {
            // Arrange
            MethodInfo method = GetMethodInfo("StaticReturnVoid");
            IJobActivator activator = CreateDummyActivator();

            // Act
            IFunctionInvoker invoker = FunctionInvokerFactory.Create(method, activator);

            // Assert
            Assert.IsType<FunctionInvoker<FunctionInvokerFactoryTests>>(invoker);
        }

        [Fact]
        public void Create_IfNoParameters_ReturnsEmptyParameterNames()
        {
            // Arrange
            MethodInfo method = GetMethodInfo("NoParameters");
            IJobActivator activator = CreateDummyActivator();

            // Act
            IFunctionInvoker invoker = FunctionInvokerFactory.Create(method, activator);

            // Assert
            Assert.NotNull(invoker);
            Assert.Equal(Enumerable.Empty<string>(), invoker.ParameterNames);
        }

        [Fact]
        public void Create_IfMultipleParameters_ReturnsParameterNames()
        {
            // Arrange
            MethodInfo method = GetMethodInfo("ParametersFooBarBaz");
            IJobActivator activator = CreateDummyActivator();

            // Act
            IFunctionInvoker invoker = FunctionInvokerFactory.Create(method, activator);

            // Assert
            Assert.NotNull(invoker);
            Assert.Equal((IEnumerable<string>)new string[] { "foo", "bar", "baz" }, invoker.ParameterNames);
        }

        [Fact]
        public void Create_IfStatic_UsesNullInstanceFactory()
        {
            // Arrange
            MethodInfo method = GetMethodInfo("StaticReturnVoid");
            IJobActivator activator = CreateDummyActivator();

            // Act
            IFunctionInvoker invoker = FunctionInvokerFactory.Create(method, activator);

            // Assert
            Assert.IsType<FunctionInvoker<FunctionInvokerFactoryTests>>(invoker);
            FunctionInvoker<FunctionInvokerFactoryTests> typedInvoker =
                (FunctionInvoker<FunctionInvokerFactoryTests>)invoker;
            Assert.IsType<NullInstanceFactory<FunctionInvokerFactoryTests>>(typedInvoker.InstanceFactory);
        }

        [Fact]
        public void Create_IfInstance_UsesActivatorInstanceFactory()
        {
            // Arrange
            MethodInfo method = GetMethodInfo("InstanceReturnVoid");
            IJobActivator activator = CreateDummyActivator();

            // Act
            IFunctionInvoker invoker = FunctionInvokerFactory.Create(method, activator);

            // Assert
            Assert.IsType<FunctionInvoker<FunctionInvokerFactoryTests>>(invoker);
            FunctionInvoker<FunctionInvokerFactoryTests> typedInvoker =
                (FunctionInvoker<FunctionInvokerFactoryTests>)invoker;
            Assert.IsType<ActivatorInstanceFactory<FunctionInvokerFactoryTests>>(typedInvoker.InstanceFactory);
        }

        [Fact]
        public void Create_IfInstanceAndMethodIsInherited_UsesReflectedType()
        {
            // Arrange
            MethodInfo method = GetMethodInfo(typeof(Subclass), "InheritedReturnVoid");
            IJobActivator activator = CreateDummyActivator();

            // Act
            IFunctionInvoker invoker = FunctionInvokerFactory.Create(method, activator);

            // Assert
            Assert.IsType<FunctionInvoker<Subclass>>(invoker);
            FunctionInvoker<Subclass> typedInvoker = (FunctionInvoker<Subclass>)invoker;
            Assert.IsType<ActivatorInstanceFactory<Subclass>>(typedInvoker.InstanceFactory);
        }

        private static IJobActivator CreateDummyActivator()
        {
            return new Mock<IJobActivator>(MockBehavior.Strict).Object;
        }

        private static MethodInfo GetMethodInfo(string name)
        {
            return GetMethodInfo(typeof(FunctionInvokerFactoryTests), name);
        }

        private static MethodInfo GetMethodInfo(Type type, string name)
        {
            return type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        }

        private static void StaticReturnVoid() { }

        private void InstanceReturnVoid() { }

        private static void NoParameters() { }

        private static void ParametersFooBarBaz(int foo, string bar, object baz) { }

        protected void InheritedReturnVoid() { }

        private class Subclass : FunctionInvokerFactoryTests
        {
        }
    }
}
