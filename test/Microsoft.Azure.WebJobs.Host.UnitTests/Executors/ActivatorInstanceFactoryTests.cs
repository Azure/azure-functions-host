// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Executors;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Executors
{
    public class ActivatorInstanceFactoryTests
    {
        [Fact]
        public void Create_DelegatesToJobActivator()
        {
            // Arrange
            object expectedInstance = new object();
            Mock<IJobActivator> activatorMock = new Mock<IJobActivator>(MockBehavior.Strict);
            activatorMock.Setup(a => a.CreateInstance<object>())
                         .Returns(expectedInstance)
                         .Verifiable();
            IJobActivator activator = activatorMock.Object;

            IFactory<object> product = CreateProductUnderTest<object>(activator);

            // Act
            object instance = product.Create();

            // Assert
            Assert.Same(expectedInstance, instance);
            activatorMock.Verify();
        }

        private static ActivatorInstanceFactory<TReflected> CreateProductUnderTest<TReflected>(IJobActivator activator)
        {
            return new ActivatorInstanceFactory<TReflected>(activator);
        }
    }
}
