// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    public class ClassPropertyGetterTests
    {
        [Fact]
        public void Create_ReturnsInstance()
        {
            // Arrange
            PropertyInfo property = typeof(Poco).GetProperty("Value");

            // Act
            IPropertyGetter<Poco, PocoProperty> getter = ClassPropertyGetter<Poco, PocoProperty>.Create(property);

            // Assert
            Assert.NotNull(getter);
        }

        [Fact]
        public void Create_IfPropertyIsNull_Throws()
        {
            // Arrange
            PropertyInfo property = null;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(() => ClassPropertyGetter<Poco, PocoProperty>.Create(property),
                "property");
        }

        [Fact]
        public void Create_IfReflectedTypeMismatches_Throws()
        {
            // Arrange
            PropertyInfo property = typeof(Poco).GetProperty("Value");

            // Act & Assert
            ExceptionAssert.ThrowsArgument(() => ClassPropertyGetter<PocoChild, PocoProperty>.Create(property),
                "property", "The property's ReflectedType must exactly match TReflected.");
        }

        [Fact]
        public void Create_IfReflectedTypeMismatchesEvenIfDeclaringType_Throws()
        {
            // Arrange
            PropertyInfo property = typeof(PocoChild).GetProperty("Value");

            // Act & Assert
            ExceptionAssert.ThrowsArgument(() => ClassPropertyGetter<Poco, PocoProperty>.Create(property), "property",
                "The property's ReflectedType must exactly match TReflected.");
        }

        [Fact]
        public void Create_IfPropertyTypeMismatches_Throws()
        {
            // Arrange
            PropertyInfo property = typeof(Poco).GetProperty("Value");

            // Act & Assert
            ExceptionAssert.ThrowsArgument(() => ClassPropertyGetter<Poco, object>.Create(property), "property",
                "The property's PropertyType must exactly match TProperty.");
        }

        [Fact]
        public void Create_IfPropertyTypeMismatchesEvenIfChildType_Throws()
        {
            // Arrange
            PropertyInfo property = typeof(Poco).GetProperty("Value");

            // Act & Assert
            ExceptionAssert.ThrowsArgument(() => ClassPropertyGetter<Poco, PocoPropertyChild>.Create(property),
                "property", "The property's PropertyType must exactly match TProperty.");
        }

        [Fact]
        public void Create_IfPropertyIsWriteOnly_Throws()
        {
            // Arrange
            PropertyInfo property = typeof(Poco).GetProperty("WriteOnlyValue");

            // Act & Assert
            ExceptionAssert.ThrowsArgument(() => ClassPropertyGetter<Poco, PocoProperty>.Create(property), "property",
                "The property must be readable.");
        }

        [Fact]
        public void Create_IfPropertyHasIndexParameters_Throws()
        {
            // Arrange
            PropertyInfo property = typeof(Poco).GetProperty("Item");

            // Act & Assert
            ExceptionAssert.ThrowsArgument(() => ClassPropertyGetter<Poco, PocoProperty>.Create(property), "property",
                "The property must not have index parameters.");
        }

        [Fact]
        public void Create_IfPropertyIsStatic_Throws()
        {
            // Arrange
            PropertyInfo property = typeof(Poco).GetProperty("StaticValue");

            // Act & Assert
            ExceptionAssert.ThrowsArgument(() => ClassPropertyGetter<Poco, PocoProperty>.Create(property), "property",
                "The property must not be static.");
        }

        [Fact]
        public void GetValue_ReturnsValue()
        {
            // Arrange
            IPropertyGetter<Poco, PocoProperty> product = CreateProductUnderTest<Poco, PocoProperty>(
                typeof(Poco).GetProperty("Value"));
            PocoProperty expected = new PocoProperty();
            Poco instance = new Poco
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
            IPropertyGetter<Poco, PocoProperty> product = CreateProductUnderTest<Poco, PocoProperty>(
                typeof(Poco).GetProperty("PrivateValue", BindingFlags.NonPublic | BindingFlags.Instance));
            PocoProperty expected = new PocoProperty();
            Poco instance = new Poco
            {
                PrivateValueAsPublic = expected
            };

            // Act
            PocoProperty actual = product.GetValue(instance);

            // Assert
            Assert.Same(expected, actual);
        }

        [Fact]
        public void GetValue_IfInstanceIsNull_Throws()
        {
            // Arrange
            IPropertyGetter<Poco, PocoProperty> product = CreateProductUnderTest<Poco, PocoProperty>(
                typeof(Poco).GetProperty("Value"));
            Poco instance = null;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(() => product.GetValue(instance), "instance");
        }

        private static ClassPropertyGetter<TReflected, TProperty> CreateProductUnderTest<TReflected, TProperty>(
            PropertyInfo property) where TReflected : class
        {
            ClassPropertyGetter<TReflected, TProperty> product =
                ClassPropertyGetter<TReflected, TProperty>.Create(property);
            Assert.NotNull(product); // Guard
            return product;
        }

        private class Poco
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

        private class PocoChild : Poco
        {
        }

        private class PocoProperty
        {
        }

        private class PocoPropertyChild : PocoProperty
        {
        }
    }
}
