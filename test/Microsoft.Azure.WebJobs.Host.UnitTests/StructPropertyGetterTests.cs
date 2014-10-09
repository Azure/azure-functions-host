// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    public class StructPropertyGetterTests
    {
        [Fact]
        public void Create_ReturnsInstance()
        {
            // Arrange
            PropertyInfo property = typeof(PocoStruct).GetProperty("Value");

            // Act
            IPropertyGetter<PocoStruct, PocoProperty> getter = StructPropertyGetter<PocoStruct, PocoProperty>.Create(
                property);

            // Assert
            Assert.NotNull(getter);
        }

        [Fact]
        public void Create_IfPropertyIsNull_Throws()
        {
            // Arrange
            PropertyInfo property = null;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(() => StructPropertyGetter<PocoStruct, PocoProperty>.Create(property),
                "property");
        }

        [Fact]
        public void Create_IfReflectedTypeMismatches_Throws()
        {
            // Arrange
            PropertyInfo property = typeof(PocoStruct).GetProperty("Value");

            // Act & Assert
            ExceptionAssert.ThrowsArgument(() => StructPropertyGetter<int, PocoProperty>.Create(property),
                "property", "The property's ReflectedType must exactly match TReflected.");
        }

        [Fact]
        public void Create_IfPropertyTypeMismatches_Throws()
        {
            // Arrange
            PropertyInfo property = typeof(PocoStruct).GetProperty("Value");

            // Act & Assert
            ExceptionAssert.ThrowsArgument(() => StructPropertyGetter<PocoStruct, object>.Create(property), "property",
                "The property's PropertyType must exactly match TProperty.");
        }

        [Fact]
        public void Create_IfPropertyTypeMismatchesEvenIfChildType_Throws()
        {
            // Arrange
            PropertyInfo property = typeof(PocoStruct).GetProperty("Value");

            // Act & Assert
            ExceptionAssert.ThrowsArgument(() => StructPropertyGetter<PocoStruct, PocoPropertyChild>.Create(property),
                "property", "The property's PropertyType must exactly match TProperty.");
        }

        [Fact]
        public void Create_IfPropertyIsWriteOnly_Throws()
        {
            // Arrange
            PropertyInfo property = typeof(PocoStruct).GetProperty("WriteOnlyValue");

            // Act & Assert
            ExceptionAssert.ThrowsArgument(() => StructPropertyGetter<PocoStruct, PocoProperty>.Create(property),
                "property", "The property must be readable.");
        }

        [Fact]
        public void Create_IfPropertyHasIndexParameters_Throws()
        {
            // Arrange
            PropertyInfo property = typeof(PocoStruct).GetProperty("Item");

            // Act & Assert
            ExceptionAssert.ThrowsArgument(() => StructPropertyGetter<PocoStruct, PocoProperty>.Create(property),
                "property", "The property must not have index parameters.");
        }

        [Fact]
        public void Create_IfPropertyIsStatic_Throws()
        {
            // Arrange
            PropertyInfo property = typeof(PocoStruct).GetProperty("StaticValue");

            // Act & Assert
            ExceptionAssert.ThrowsArgument(() => StructPropertyGetter<PocoStruct, PocoProperty>.Create(property),
                "property", "The property must not be static.");
        }

        [Fact]
        public void GetValue_ReturnsValue()
        {
            // Arrange
            IPropertyGetter<PocoStruct, PocoProperty> product = CreateProductUnderTest<PocoStruct, PocoProperty>(
                typeof(PocoStruct).GetProperty("Value"));
            PocoProperty expected = new PocoProperty();
            PocoStruct instance = new PocoStruct
            {
                Value = expected
            };

            // Act
            PocoProperty actual = product.GetValue(instance);

            // Assert
            Assert.Same(expected, actual);
        }

        [Fact]
        public void GetValue_IfPrivateProperty_ReturnsValue()
        {
            // Arrange
            IPropertyGetter<PocoStruct, PocoProperty> product = CreateProductUnderTest<PocoStruct, PocoProperty>(
                typeof(PocoStruct).GetProperty("PrivateValue", BindingFlags.NonPublic | BindingFlags.Instance));
            PocoProperty expected = new PocoProperty();
            PocoStruct instance = new PocoStruct
            {
                PrivateValueAsPublic = expected
            };

            // Act
            PocoProperty actual = product.GetValue(instance);

            // Assert
            Assert.Same(expected, actual);
        }

        private static StructPropertyGetter<TReflected, TProperty> CreateProductUnderTest<TReflected, TProperty>(
            PropertyInfo property) where TReflected : struct
        {
            StructPropertyGetter<TReflected, TProperty> product =
                StructPropertyGetter<TReflected, TProperty>.Create(property);
            Assert.NotNull(product); // Guard
            return product;
        }

        private struct PocoStruct
        {
            public PocoProperty Value { get; set; }

            private PocoProperty PrivateValue { get; set; }

            public static PocoProperty StaticValue { get; set; }

            public PocoProperty WriteOnlyValue { set { } }

            public PocoProperty PrivateValueAsPublic
            {
                get { return PrivateValue; }
                set { PrivateValue = value; }
            }

            public PocoProperty this[string key]
            {
                get
                {
                    return null;
                }
                set { }
            }
        }

        private class PocoProperty
        {
        }

        private class PocoPropertyChild : PocoProperty
        {
        }
    }
}
