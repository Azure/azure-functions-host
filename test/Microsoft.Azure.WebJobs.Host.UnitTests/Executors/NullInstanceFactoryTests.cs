// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Executors;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Executors
{
    public class NullInstanceFactoryTests
    {
        [Fact]
        public void Create_ReturnsNull()
        {
            // Arrange
            IFactory<object> product = CreateProductUnderTest<object>();

            // Act
            object instance = product.Create();

            // Assert
            Assert.Null(instance);
        }

        private static NullInstanceFactory<TReflected> CreateProductUnderTest<TReflected>()
        {
            return NullInstanceFactory<TReflected>.Instance;
        }
    }
}
