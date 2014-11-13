// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.TestCommon;
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

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(() => FunctionInvokerFactory.Create(method), "method");
        }

        [Fact]
        public void Create_ReturnsFunctionInvoker()
        {
            // Arrange
            MethodInfo method = GetMethodInfo("StaticReturnVoid");

            // Act
            IFunctionInvoker invoker = FunctionInvokerFactory.Create(method);

            // Assert
            Assert.IsType<FunctionInvoker<FunctionInvokerFactoryTests>>(invoker);
        }

        [Fact]
        public void Create_IfNoParameters_ReturnsEmptyParameterNames()
        {
            // Arrange
            MethodInfo method = GetMethodInfo("NoParameters");

            // Act
            IFunctionInvoker invoker = FunctionInvokerFactory.Create(method);

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
            IFunctionInvoker invoker = FunctionInvokerFactory.Create(method);

            // Assert
            Assert.NotNull(invoker);
            Assert.Equal((IEnumerable<string>)new string[] { "foo", "bar", "baz" }, invoker.ParameterNames);
        }

        [Fact]
        public void Create_IfStatic_UsesNullInstanceFactory()
        {
            // Arrange
            MethodInfo method = GetMethodInfo("StaticReturnVoid");

            // Act
            IFunctionInvoker invoker = FunctionInvokerFactory.Create(method);

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

            // Act
            IFunctionInvoker invoker = FunctionInvokerFactory.Create(method);

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

            // Act
            IFunctionInvoker invoker = FunctionInvokerFactory.Create(method);

            // Assert
            Assert.IsType<FunctionInvoker<Subclass>>(invoker);
            FunctionInvoker<Subclass> typedInvoker = (FunctionInvoker<Subclass>)invoker;
            Assert.IsType<ActivatorInstanceFactory<Subclass>>(typedInvoker.InstanceFactory);
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
