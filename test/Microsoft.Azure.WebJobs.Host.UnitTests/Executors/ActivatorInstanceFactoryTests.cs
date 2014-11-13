// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Executors;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Executors
{
    public class ActivatorInstanceFactoryTests
    {
        [Fact]
        public void Create_ReturnsNonNull()
        {
            // Arrange
            IFactory<object> product = CreateProductUnderTest<object>();

            // Act
            object instance = product.Create();

            // Assert
            Assert.NotNull(instance);
        }

        [Fact]
        public void Create_ReturnsNewInstance()
        {
            // Arrange
            IFactory<object> product = CreateProductUnderTest<object>();
            object originalInstance = product.Create();

            // Act
            object instance = product.Create();

            // Assert
            Assert.NotNull(instance);
            Assert.NotSame(originalInstance, instance);
        }

        private static ActivatorInstanceFactory<TReflected> CreateProductUnderTest<TReflected>()
        {
            return new ActivatorInstanceFactory<TReflected>();
        }
    }
}
