using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.TestCommon;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests
{
    public class PolymorphicJsonConverterTests
    {
        [Fact]
        public void Constructor_IfTypePropertyNameIsNull_Throws()
        {
            // Arrange
            string typePropertyName = null;
            IDictionary<string, Type> typeMapping = new Dictionary<string, Type>();

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(() => CreateProductUnderTest(typePropertyName, typeMapping),
                "typePropertyName");
        }

        [Fact]
        public void Constructor_IfTypeMappingIsNull_Throws()
        {
            // Arrange
            string typePropertyName = "IgnoreProperty";
            IDictionary<string, Type> typeMapping = null;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(() => CreateProductUnderTest(typePropertyName, typeMapping),
                "typeMapping");
        }

        [Fact]
        public void TypePropertyName_ReturnsSpecifiedInstance()
        {
            // Arrange
            string expectedTypePropertyName = "IgnoreProperty";
            IDictionary<string, Type> typeMapping = new Dictionary<string, Type>();
            PolymorphicJsonConverter product = CreateProductUnderTest(expectedTypePropertyName, typeMapping);

            // Act
            string typePropertyName = product.TypePropertyName;

            // Assert
            Assert.Same(expectedTypePropertyName, typePropertyName);
        }

        [Fact]
        public void CanConvert_IfObjectTypeIsInMapping_ReturnsTrue()
        {
            // Arrange
            Type typeInMapping = typeof(object);
            IDictionary<string, Type> typeMapping = new Dictionary<string, Type>
            {
                { "IgnoreTypeName", typeInMapping }
            };
            JsonConverter product = CreateProductUnderTest(typeMapping);

            // Act
            bool canConvert = product.CanConvert(typeInMapping);

            // Assert
            Assert.True(canConvert);
        }

        [Fact]
        public void CanConvert_IfObjectTypeIsNotInMapping_ReturnsFalse()
        {
            // Arrange
            Type typeInMapping = typeof(string);
            IDictionary<string, Type> typeMapping = new Dictionary<string, Type>
            {
                { "IgnoreTypeName", typeInMapping }
            };
            JsonConverter product = CreateProductUnderTest(typeMapping);
            Type typeNotInMapping = typeof(int);

            // Act
            bool canConvert = product.CanConvert(typeNotInMapping);

            // Assert
            Assert.False(canConvert);
        }

        [Fact]
        public void ReadJson_IfTypeNameIsPresent_ReturnsObjectOfTypeNameType()
        {
            // Arrange
            string typePropertyName = "TheTypeProperty";
            string typeName = "TheTypeName";
            IDictionary<string, Type> typeMapping = new Dictionary<string, Type>
            {
                { typeName, typeof(TypeWithFoo) }
            };
            JsonConverter product = CreateProductUnderTest(typePropertyName, typeMapping);
            string expectedFooValue = "IgnoreFoo";
            JObject json = new JObject();
            json.Add(typePropertyName, new JValue(typeName));
            json.Add("Foo", new JValue(expectedFooValue));

            using (JsonReader reader = CreateReader(json))
            {
                Type objectType = typeof(object);
                object existingValue = null;
                JsonSerializer serializer = new JsonSerializer();

                // Act
                object value = product.ReadJson(reader, objectType, existingValue, serializer);

                // Assert
                Assert.IsType<TypeWithFoo>(value);
                TypeWithFoo valueWithFoo = (TypeWithFoo)value;
                Assert.Equal(expectedFooValue, valueWithFoo.Foo);
            }
        }

        [Fact]
        public void ReadJson_IfTypeNameIsMissing_ReturnsObjectOfObjectType()
        {
            // Arrange
            JsonConverter product = CreateProductUnderTest();
            string expectedFooValue = "IgnoreFoo";
            JObject json = new JObject();
            json.Add("Foo", new JValue(expectedFooValue));

            using (JsonReader reader = CreateReader(json))
            {
                Type objectType = typeof(TypeWithFoo);
                object existingValue = null;
                JsonSerializer serializer = new JsonSerializer();

                // Act
                object value = product.ReadJson(reader, objectType, existingValue, serializer);

                // Assert
                Assert.IsType<TypeWithFoo>(value);
                TypeWithFoo valueWithFoo = (TypeWithFoo)value;
                Assert.Equal(expectedFooValue, valueWithFoo.Foo);
            }
        }

        [Fact]
        public void ReadJson_IfTokenIsNull_ReturnsNull()
        {
            // Arrange
            JsonConverter product = CreateProductUnderTest();

            using (JsonReader reader = CreateReader(new JValue((object)null)))
            {
                Type objectType = typeof(object);
                object existingValue = null;
                JsonSerializer serializer = new JsonSerializer();

                // Act
                object value = product.ReadJson(reader, objectType, existingValue, serializer);

                // Assert
                Assert.Null(value);
            }
        }

        [Fact]
        public void ReadJson_IfTokenIsValue_Throws()
        {
            // Arrange
            JsonConverter product = CreateProductUnderTest();
            JValue json = new JValue("IgnoreValue");

            using (JsonReader reader = CreateReader(json))
            {
                Type objectType = typeof(object);
                object existingValue = null;
                JsonSerializer serializer = new JsonSerializer();

                // Act & Assert
                Assert.Throws<JsonSerializationException>(() => product.ReadJson(reader, objectType, existingValue, serializer));
            }
        }

        [Fact]
        public void ReadJson_IfTypeNameIsNotAValue_ReturnsObjectOfObjectType()
        {
            // Arrange
            string typePropertyName = "TheTypeProperty";
            string typeName = "TheTypeName";
            IDictionary<string, Type> typeMapping = new Dictionary<string, Type>
            {
                { typeName, typeof(TypeWithFooAndBar) }
            };
            JsonConverter product = CreateProductUnderTest(typePropertyName, typeMapping);
            string expectedFooValue = "IgnoreFoo";
            JObject json = new JObject();
            json.Add(typePropertyName, new JValue(0));
            json.Add("Foo", new JValue(expectedFooValue));

            using (JsonReader reader = CreateReader(json))
            {
                Type objectType = typeof(TypeWithFoo);
                object existingValue = null;
                JsonSerializer serializer = new JsonSerializer();

                // Act
                object value = product.ReadJson(reader, objectType, existingValue, serializer);

                // Assert
                Assert.IsType<TypeWithFoo>(value);
                TypeWithFoo valueWithFoo = (TypeWithFoo)value;
                Assert.Equal(expectedFooValue, valueWithFoo.Foo);
            }
        }

        [Fact]
        public void ReadJson_IfTypeNameIsNotInMapping_ReturnsObjectOfObjectType()
        {
            // Arrange
            string typePropertyName = "TheTypeProperty";
            string typeName = "TheTypeName";
            IDictionary<string, Type> typeMapping = new Dictionary<string, Type>
            {
                { typeName, typeof(TypeWithFooAndBar) }
            };
            JsonConverter product = CreateProductUnderTest(typePropertyName, typeMapping);
            string expectedFooValue = "IgnoreFoo";
            JObject json = new JObject();
            json.Add(typePropertyName, new JValue("TypeNameNotInMapping"));
            json.Add("Foo", new JValue(expectedFooValue));

            using (JsonReader reader = CreateReader(json))
            {
                Type objectType = typeof(TypeWithFoo);
                object existingValue = null;
                JsonSerializer serializer = new JsonSerializer();

                // Act
                object value = product.ReadJson(reader, objectType, existingValue, serializer);

                // Assert
                Assert.IsType<TypeWithFoo>(value);
                TypeWithFoo valueWithFoo = (TypeWithFoo)value;
                Assert.Equal(expectedFooValue, valueWithFoo.Foo);
            }
        }

        [Fact]
        public void ReadJson_IfTypeNameValueIsNotAString_ReturnsObjectOfObjectType()
        {
            // Arrange
            string typePropertyName = "TheTypeProperty";
            string typeName = "TheTypeName";
            IDictionary<string, Type> typeMapping = new Dictionary<string, Type>
            {
                { typeName, typeof(TypeWithFooAndBar) }
            };
            JsonConverter product = CreateProductUnderTest(typePropertyName, typeMapping);
            string expectedFooValue = "IgnoreFoo";
            JObject json = new JObject();
            json.Add(typePropertyName, new JArray());
            json.Add("Foo", new JValue(expectedFooValue));

            using (JsonReader reader = CreateReader(json))
            {
                Type objectType = typeof(TypeWithFoo);
                object existingValue = null;
                JsonSerializer serializer = new JsonSerializer();

                // Act
                object value = product.ReadJson(reader, objectType, existingValue, serializer);

                // Assert
                Assert.IsType<TypeWithFoo>(value);
                TypeWithFoo valueWithFoo = (TypeWithFoo)value;
                Assert.Equal(expectedFooValue, valueWithFoo.Foo);
            }
        }

        [Fact]
        public void ReadJson_IfReaderIsNull_Throws()
        {
            // Arrange
            JsonConverter product = CreateProductUnderTest();
            JsonReader reader = null;
            Type objectType = typeof(object);
            object existingValue = null;
            JsonSerializer serializer = null;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(() => product.ReadJson(reader, objectType, existingValue, serializer), "reader");
        }

        [Fact]
        public void ReadJson_IfObjectTypeIsNull_Throws()
        {
            // Arrange
            JsonConverter product = CreateProductUnderTest();
            JsonReader reader = CreateDummyReader();
            Type objectType = null;
            object existingValue = null;
            JsonSerializer serializer = new JsonSerializer();

            // Act & Assert
            NotSupportedException exception = Assert.Throws<NotSupportedException>(() => product.ReadJson(reader, objectType, existingValue, serializer));
            Assert.Equal(
                "PolymorphicJsonConverter does not support deserializing without specifying a default objectType.",
                exception.Message);
        }

        [Fact]
        public void ReadJson_IfSerializerIsNull_Throws()
        {
            // Arrange
            JsonConverter product = CreateProductUnderTest();
            JsonReader reader = CreateDummyReader();
            Type objectType = typeof(object);
            object existingValue = null;
            JsonSerializer serializer = null;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(() => product.ReadJson(reader, objectType, existingValue, serializer), "serializer");
        }

        [Fact]
        public void WriteJson_AddsTypeNameToObject()
        {
            // Arrange
            string typePropertyName = "TypeProperty";
            string expectedTypeName = "TheTypeName";
            string expectedFooValue = "TheFoo";
            IDictionary<string, Type> typeMapping = new Dictionary<string, Type>
            {
                { expectedTypeName, typeof(TypeWithFoo) }
            };
            JsonConverter product = CreateProductUnderTest(typePropertyName, typeMapping);
            JToken token;

            using (JTokenWriter writer = new JTokenWriter())
            {
                TypeWithFoo value = new TypeWithFoo
                {
                    Foo = expectedFooValue
                };
                JsonSerializer serializer = new JsonSerializer();

                // Act
                product.WriteJson(writer, value, serializer);

                token = writer.Token;
            }

            // Assert
            string json = GetJson(token);
            JObject expectedObject = new JObject();
            expectedObject.Add(typePropertyName, new JValue(expectedTypeName));
            expectedObject.Add("Foo", expectedFooValue);
            string expectedJson = GetJson(expectedObject);
            Assert.Equal(expectedJson, json);
        }

        [Fact]
        public void WriteJson_IfValueIsNull_WritesNull()
        {
            // Arrange
            JsonConverter product = CreateProductUnderTest();
            JToken token;

            using (JTokenWriter writer = new JTokenWriter())
            {
                object value = null;
                JsonSerializer serializer = new JsonSerializer();

                // Act
                product.WriteJson(writer, value, serializer);

                token = writer.Token;
            }

            // Assert
            Assert.NotNull(token);
            Assert.IsType<JValue>(token);
            JValue tokenValue = (JValue)token;
            Assert.Null(tokenValue.Value);
        }

        [Fact]
        public void WriteJson_IfTypeNamePropertyAlreadyExists_ReplacesExistingProperty()
        {
            // Arrange
            string expectedTypeName = "OfficialTypeName";
            string expectedFooValue = "TheFoo";
            IDictionary<string, Type> typeMapping = new Dictionary<string, Type>
            {
                { expectedTypeName, typeof(TypeWithFooAndType) }
            };
            JsonConverter product = CreateProductUnderTest("Type", typeMapping);
            JToken token;

            using (JTokenWriter writer = new JTokenWriter())
            {
                TypeWithFooAndType value = new TypeWithFooAndType
                {
                    Type = "OtherTypeName",
                    Foo = expectedFooValue
                };
                JsonSerializer serializer = new JsonSerializer();

                // Act
                product.WriteJson(writer, value, serializer);

                token = writer.Token;
            }

            // Assert
            string json = GetJson(token);
            JObject expectedObject = new JObject();
            expectedObject.Add("Type", new JValue(expectedTypeName));
            expectedObject.Add("Foo", expectedFooValue);
            string expectedJson = GetJson(expectedObject);
            Assert.Equal(expectedJson, json);
        }

        [Fact]
        public void WriteJson_IfTypeIsNotInMapping_DoesNotAddTypeName()
        {
            // Arrange
            string expectedFooValue = "TheFoo";
            JsonConverter product = CreateProductUnderTest();
            JToken token;

            using (JTokenWriter writer = new JTokenWriter())
            {
                TypeWithFoo value = new TypeWithFoo
                {
                    Foo = expectedFooValue
                };
                JsonSerializer serializer = new JsonSerializer();

                // Act
                product.WriteJson(writer, value, serializer);

                token = writer.Token;
            }

            // Assert
            string json = GetJson(token);
            JObject expectedObject = new JObject();
            expectedObject.Add("Foo", expectedFooValue);
            string expectedJson = GetJson(expectedObject);
            Assert.Equal(expectedJson, json);
        }

        [Fact]
        public void WriteJson_IfWriterIsNull_Throws()
        {
            // Arrange
            JsonConverter product = CreateProductUnderTest();
            JsonWriter writer = null;
            object value = new object();
            JsonSerializer serializer = new JsonSerializer();

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(() => product.WriteJson(writer, value, serializer), "writer");
        }

        [Fact]
        public void WriteJson_IfSerializerIsNull_Throws()
        {
            // Arrange
            JsonConverter product = CreateProductUnderTest();
            JsonWriter writer = CreateDummyWriter();
            object value = new object();
            JsonSerializer serializer = null;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(() => product.WriteJson(writer, value, serializer), "serializer");
        }

        [Fact]
        public void GetTypeMapping_IfJsonTypeNameAttributeIsPresent_UsesAttributeTypeName()
        {
            // Act
            IDictionary<string, Type> typeMapping = PolymorphicJsonConverter.GetTypeMapping<TypeWithNameInAttribute>();

            // Assert
            Assert.NotNull(typeMapping);
            Assert.Equal(1, typeMapping.Count);
            Assert.True(typeMapping.ContainsKey("TypeNameInAttribute"));
            Assert.Equal(typeof(TypeWithNameInAttribute), typeMapping["TypeNameInAttribute"]);
        }

        [Fact]
        public void GetTypeMapping_IfJsonTypeNameAttributeIsAbsent_UsesGetTypeTypeName()
        {
            // Act
            IDictionary<string, Type> typeMapping = PolymorphicJsonConverter.GetTypeMapping<TypeWithoutCustomName>();

            // Assert
            Assert.NotNull(typeMapping);
            Assert.Equal(1, typeMapping.Count);
            Assert.True(typeMapping.ContainsKey("TypeWithoutCustomName"));
            Assert.Equal(typeof(TypeWithoutCustomName), typeMapping["TypeWithoutCustomName"]);
        }

        [Fact]
        public void GetTypeMapping_IncludesDerivedTypes()
        {
            // Act
            IDictionary<string, Type> typeMapping = PolymorphicJsonConverter.GetTypeMapping<TypeWithTwoDerivedTypes>();

            // Assert
            Assert.NotNull(typeMapping);
            Assert.Equal(3, typeMapping.Count);
            Assert.True(typeMapping.ContainsKey("TypeWithTwoDerivedTypes"));
            Assert.Equal(typeof(TypeWithTwoDerivedTypes), typeMapping["TypeWithTwoDerivedTypes"]);
            Assert.True(typeMapping.ContainsKey("ChildTypeWithoutCustomName"));
            Assert.Equal(typeof(ChildTypeWithoutCustomName), typeMapping["ChildTypeWithoutCustomName"]);
            Assert.True(typeMapping.ContainsKey("CustomTypeName"));
            Assert.Equal(typeof(GrandchildTypeWithCustomName), typeMapping["CustomTypeName"]);
        }

        private static JsonReader CreateDummyReader()
        {
            return new Mock<JsonReader>(MockBehavior.Strict).Object;
        }

        private static JsonWriter CreateDummyWriter()
        {
            return new Mock<JsonWriter>(MockBehavior.Strict).Object;
        }

        private static PolymorphicJsonConverter CreateProductUnderTest()
        {
            IDictionary<string, Type> hierarchyTypes = new Dictionary<string, Type>();
            return CreateProductUnderTest(hierarchyTypes);
        }

        private static PolymorphicJsonConverter CreateProductUnderTest(IDictionary<string, Type> typeMapping)
        {
            string typePropertyName = "IgnoreType";
            return CreateProductUnderTest(typePropertyName, typeMapping);
        }

        private static PolymorphicJsonConverter CreateProductUnderTest(string typePropertyName, IDictionary<string, Type> typeMapping)
        {
            return new PolymorphicJsonConverter(typePropertyName, typeMapping);
        }

        private static JsonReader CreateReader(JToken token)
        {
            JsonReader reader = token.CreateReader();
            bool read = reader.Read();
            Assert.True(read);
            return reader;
        }

        private static string GetJson(JToken token)
        {
            return token.ToString(Formatting.None);
        }

        [JsonConverter(typeof(DetectCircularReferenceConverter))]
        private class TypeWithFoo
        {
            public string Foo { get; set; }
        }

        private class TypeWithFooAndBar : TypeWithFoo
        {
            public string Bar { get; set; }
        }

        private class TypeWithFooAndType : TypeWithFoo
        {
            public string Type { get; set; }
        }

        [JsonTypeName("TypeNameInAttribute")]
        private class TypeWithNameInAttribute
        {

        }

        private class TypeWithTwoDerivedTypes
        {

        }

        private class ChildTypeWithoutCustomName : TypeWithTwoDerivedTypes
        {
        }

        [JsonTypeName("CustomTypeName")]
        private class GrandchildTypeWithCustomName : ChildTypeWithoutCustomName
        {
        }

        private class TypeWithoutCustomName
        {
        }

        private class DetectCircularReferenceConverter : PolymorphicJsonConverter
        {
            public DetectCircularReferenceConverter()
                : base("Unused", new Dictionary<string, Type>())
            {
            }
        }
    }
}
