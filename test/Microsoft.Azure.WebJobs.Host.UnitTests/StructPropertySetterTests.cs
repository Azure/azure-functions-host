// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Reflection;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    public class StructPropertySetterTests
    {
        [Fact]
        public void Create_ReturnsInstance()
        {
            // Arrange
            PropertyInfo property = typeof(PocoStruct).GetProperty("Value");

            // Act
            IPropertySetter<PocoStruct, PocoProperty> setter = StructPropertySetter<PocoStruct, PocoProperty>.Create(
                property);

            // Assert
            Assert.NotNull(setter);
        }

        [Fact]
        public void Create_IfPropertyIsNull_Throws()
        {
            // Arrange
            PropertyInfo property = null;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(() => StructPropertySetter<PocoStruct, PocoProperty>.Create(property),
                "property");
        }

        [Fact]
        public void Create_IfReflectedTypeMismatches_Throws()
        {
            // Arrange
            PropertyInfo property = typeof(PocoStruct).GetProperty("Value");

            // Act & Assert
            ExceptionAssert.ThrowsArgument(() => StructPropertySetter<int, PocoProperty>.Create(property),
                "property", "The property's ReflectedType must exactly match TReflected.");
        }

        [Fact]
        public void Create_IfPropertyTypeMismatches_Throws()
        {
            // Arrange
            PropertyInfo property = typeof(PocoStruct).GetProperty("Value");

            // Act & Assert
            ExceptionAssert.ThrowsArgument(() => StructPropertySetter<PocoStruct, object>.Create(property), "property",
                "The property's PropertyType must exactly match TProperty.");
        }

        [Fact]
        public void Create_IfPropertyTypeMismatchesEvenIfChildType_Throws()
        {
            // Arrange
            PropertyInfo property = typeof(PocoStruct).GetProperty("Value");

            // Act & Assert
            ExceptionAssert.ThrowsArgument(() => StructPropertySetter<PocoStruct, PocoPropertyChild>.Create(property),
                "property", "The property's PropertyType must exactly match TProperty.");
        }

        [Fact]
        public void Create_IfPropertyIsReadOnly_Throws()
        {
            // Arrange
            PropertyInfo property = typeof(PocoStruct).GetProperty("ReadOnlyValue");

            // Act & Assert
            ExceptionAssert.ThrowsArgument(() => StructPropertySetter<PocoStruct, PocoProperty>.Create(property),
                "property", "The property must be writable.");
        }

        [Fact]
        public void Create_IfPropertyHasIndexParameters_Throws()
        {
            // Arrange
            PropertyInfo property = typeof(PocoStruct).GetProperty("Item");

            // Act & Assert
            ExceptionAssert.ThrowsArgument(() => StructPropertySetter<PocoStruct, PocoProperty>.Create(property),
                "property", "The property must not have index parameters.");
        }

        [Fact]
        public void Create_IfPropertyIsStatic_Throws()
        {
            // Arrange
            PropertyInfo property = typeof(PocoStruct).GetProperty("StaticValue");

            // Act & Assert
            ExceptionAssert.ThrowsArgument(() => StructPropertySetter<PocoStruct, PocoProperty>.Create(property),
                "property", "The property must not be static.");
        }

        [Fact]
        public void SetValue_UpdatesValue()
        {
            // Arrange
            IPropertySetter<PocoStruct, PocoProperty> product = CreateProductUnderTest<PocoStruct, PocoProperty>(
                typeof(PocoStruct).GetProperty("Value"));
            PocoStruct instance = new PocoStruct();
            PocoProperty expected = new PocoProperty();

            // Act
            product.SetValue(ref instance, expected);

            // Assert
            PocoProperty actual = instance.Value;
            Assert.Same(expected, actual);
        }

        [Fact]
        public void SetValue_IfPrivateProperty_UpdatesValue()
        {
            // Arrange
            IPropertySetter<PocoStruct, PocoProperty> product = CreateProductUnderTest<PocoStruct, PocoProperty>(
                typeof(PocoStruct).GetProperty("PrivateValue", BindingFlags.NonPublic | BindingFlags.Instance));
            PocoStruct instance = new PocoStruct();
            PocoProperty expected = new PocoProperty();

            // Act
            product.SetValue(ref instance, expected);

            // Assert
            PocoProperty actual = instance.PrivateValueAsPublic;
            Assert.Same(expected, actual);
        }

        private static StructPropertySetter<TReflected, TProperty> CreateProductUnderTest<TReflected, TProperty>(
            PropertyInfo property) where TReflected : struct
        {
            StructPropertySetter<TReflected, TProperty> product =
                StructPropertySetter<TReflected, TProperty>.Create(property);
            Assert.NotNull(product); // Guard
            return product;
        }

        private struct PocoStruct
        {
            public PocoProperty Value { get; set; }

            private PocoProperty PrivateValue { get; set; }

            public static PocoProperty StaticValue { get; set; }

            public PocoProperty ReadOnlyValue
            {
                get { return null; }
            }

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
