// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Executors;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Executors
{
    public class DefaultJobActivatorTests
    {
        [Fact]
        public void Create_ReturnsNonNull()
        {
            // Arrange
            IJobActivator product = CreateProductUnderTest();

            // Act
            object instance = product.CreateInstance<object>();

            // Assert
            Assert.NotNull(instance);
        }

        [Fact]
        public void Create_ReturnsNewInstance()
        {
            // Arrange
            IJobActivator product = CreateProductUnderTest();
            object originalInstance = product.CreateInstance<object>();

            // Act
            object instance = product.CreateInstance<object>();

            // Assert
            Assert.NotNull(instance);
            Assert.NotSame(originalInstance, instance);
        }

        private static DefaultJobActivator CreateProductUnderTest()
        {
            return DefaultJobActivator.Instance;
        }
    }
}
