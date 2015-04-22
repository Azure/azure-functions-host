// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Reflection;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    public class ClassPropertySetterTests
    {
        [Fact]
        public void Create_ReturnsInstance()
        {
            // Arrange
            PropertyInfo property = typeof(Poco).GetProperty("Value");

            // Act
            IPropertySetter<Poco, PocoProperty> setter = ClassPropertySetter<Poco, PocoProperty>.Create(property);

            // Assert
            Assert.NotNull(setter);
        }

        [Fact]
        public void Create_IfPropertyIsNull_Throws()
        {
            // Arrange
            PropertyInfo property = null;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(() => ClassPropertySetter<Poco, PocoProperty>.Create(property),
                "property");
        }

        [Fact]
        public void Create_IfReflectedTypeMismatches_Throws()
        {
            // Arrange
            PropertyInfo property = typeof(Poco).GetProperty("Value");

            // Act & Assert
            ExceptionAssert.ThrowsArgument(() => ClassPropertySetter<PocoChild, PocoProperty>.Create(property),
                "property", "The property's ReflectedType must exactly match TReflected.");
        }

        [Fact]
        public void Create_IfReflectedTypeMismatchesEvenIfDeclaringType_Throws()
        {
            // Arrange
            PropertyInfo property = typeof(PocoChild).GetProperty("Value");

            // Act & Assert
            ExceptionAssert.ThrowsArgument(() => ClassPropertySetter<Poco, PocoProperty>.Create(property), "property",
                "The property's ReflectedType must exactly match TReflected.");
        }

        [Fact]
        public void Create_IfPropertyTypeMismatches_Throws()
        {
            // Arrange
            PropertyInfo property = typeof(Poco).GetProperty("Value");

            // Act & Assert
            ExceptionAssert.ThrowsArgument(() => ClassPropertySetter<Poco, object>.Create(property), "property",
                "The property's PropertyType must exactly match TProperty.");
        }

        [Fact]
        public void Create_IfPropertyTypeMismatchesEvenIfChildType_Throws()
        {
            // Arrange
            PropertyInfo property = typeof(Poco).GetProperty("Value");

            // Act & Assert
            ExceptionAssert.ThrowsArgument(() => ClassPropertySetter<Poco, PocoPropertyChild>.Create(property),
                "property", "The property's PropertyType must exactly match TProperty.");
        }

        [Fact]
        public void Create_IfPropertyIsReadOnly_Throws()
        {
            // Arrange
            PropertyInfo property = typeof(Poco).GetProperty("ReadOnlyValue");

            // Act & Assert
            ExceptionAssert.ThrowsArgument(() => ClassPropertySetter<Poco, PocoProperty>.Create(property), "property",
                "The property must be writable.");
        }

        [Fact]
        public void Create_IfPropertyHasIndexParameters_Throws()
        {
            // Arrange
            PropertyInfo property = typeof(Poco).GetProperty("Item");

            // Act & Assert
            ExceptionAssert.ThrowsArgument(() => ClassPropertySetter<Poco, PocoProperty>.Create(property), "property",
                "The property must not have index parameters.");
        }

        [Fact]
        public void Create_IfPropertyIsStatic_Throws()
        {
            // Arrange
            PropertyInfo property = typeof(Poco).GetProperty("StaticValue");

            // Act & Assert
            ExceptionAssert.ThrowsArgument(() => ClassPropertySetter<Poco, PocoProperty>.Create(property), "property",
                "The property must not be static.");
        }

        [Fact]
        public void SetValue_UpdatesValue()
        {
            // Arrange
            IPropertySetter<Poco, PocoProperty> product = CreateProductUnderTest<Poco, PocoProperty>(
                typeof(Poco).GetProperty("Value"));
            Poco instance = new Poco();
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
            IPropertySetter<Poco, PocoProperty> product = CreateProductUnderTest<Poco, PocoProperty>(
                typeof(Poco).GetProperty("PrivateValue", BindingFlags.NonPublic | BindingFlags.Instance));
            Poco instance = new Poco();
            PocoProperty expected = new PocoProperty();

            // Act
            product.SetValue(ref instance, expected);

            // Assert
            PocoProperty actual = instance.PrivateValueAsPublic;
            Assert.Same(expected, actual);
        }

        [Fact]
        public void SetValue_IfInstanceIsNull_Throws()
        {
            // Arrange
            IPropertySetter<Poco, PocoProperty> product = CreateProductUnderTest<Poco, PocoProperty>(
                typeof(Poco).GetProperty("Value"));
            Poco instance = null;
            PocoProperty value = new PocoProperty();

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(() => product.SetValue(ref instance, value), "instance");
        }

        private static ClassPropertySetter<TReflected, TProperty> CreateProductUnderTest<TReflected, TProperty>(
            PropertyInfo property) where TReflected : class
        {
            ClassPropertySetter<TReflected, TProperty> product =
                ClassPropertySetter<TReflected, TProperty>.Create(property);
            Assert.NotNull(product); // Guard
            return product;
        }

        private class Poco
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
