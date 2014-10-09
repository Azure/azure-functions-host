// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Reflection;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Tables
{
    public class PropertyAccessorFactoryTests
    {
        [Fact]
        public void CreateGetter_IfClass_ReturnsInstance()
        {
            // Arrange
            PropertyInfo propertyInfo = typeof(Poco).GetProperty("Value");

            // Act
            IPropertyGetter<Poco, string> manager = PropertyAccessorFactory<Poco>.CreateGetter<string>(propertyInfo);

            // Assert
            Assert.NotNull(manager);
        }

        [Fact]
        public void CreateGetter_IfStruct_ReturnsInstance()
        {
            // Arrange
            PropertyInfo propertyInfo = typeof(PocoStruct).GetProperty("Value");

            // Act
            IPropertyGetter<PocoStruct, string> manager = PropertyAccessorFactory<PocoStruct>.CreateGetter<string>(
                propertyInfo);

            // Assert
            Assert.NotNull(manager);
        }

        [Fact]
        public void CreateSetter_IfClass_ReturnsInstance()
        {
            // Arrange
            PropertyInfo propertyInfo = typeof(Poco).GetProperty("Value");

            // Act
            IPropertySetter<Poco, string> manager = PropertyAccessorFactory<Poco>.CreateSetter<string>(propertyInfo);

            // Assert
            Assert.NotNull(manager);
        }

        [Fact]
        public void CreateSetter_IfStruct_ReturnsInstance()
        {
            // Arrange
            PropertyInfo propertyInfo = typeof(PocoStruct).GetProperty("Value");

            // Act
            IPropertySetter<PocoStruct, string> manager = PropertyAccessorFactory<PocoStruct>.CreateSetter<string>(
                propertyInfo);

            // Assert
            Assert.NotNull(manager);
        }

        private class Poco
        {
            public string Value { get; set; }
        }

        private struct PocoStruct
        {
            public string Value { get; set; }
        }
    }
}
