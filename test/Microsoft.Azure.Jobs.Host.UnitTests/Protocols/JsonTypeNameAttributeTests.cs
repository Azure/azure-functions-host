// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Protocols
{
    public class JsonTypeNameAttributeTests
    {
        [Fact]
        public static void Constructor_IfTypeNameIsNull_Throws()
        {
            // Arrange
            string typeName = null;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(() => CreateProductUnderTest(typeName), "typeName");
        }

        [Fact]
        public static void TypeName_IsSpecifiedInstance()
        {
            // Arrange
            string expectedTypeName = "IgnoreName";
            JsonTypeNameAttribute product = CreateProductUnderTest(expectedTypeName);

            // Act
            string typeName = product.TypeName;

            // Assert
            Assert.Same(expectedTypeName, typeName);
        }

        private static JsonTypeNameAttribute CreateProductUnderTest(string typeName)
        {
            return new JsonTypeNameAttribute(typeName);
        }
    }
}
