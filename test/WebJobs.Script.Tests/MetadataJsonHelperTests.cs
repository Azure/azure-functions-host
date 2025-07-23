// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public sealed class MetadataJsonHelperTests
    {
        [Fact]
        public void CreateJObjectWithSanitizedPropertyValue_NullJsonObject_ThrowsArgumentNullException()
        {
            JObject jsonObject = null;
            ImmutableHashSet<string> propertyNames = ["sensitiveProperty"];

            Assert.Throws<ArgumentNullException>(() => MetadataJsonHelper.SanitizeProperties(jsonObject, propertyNames));
        }

        [Fact]
        public void CreateJObjectWithSanitizedPropertyValue_NullPropertyNames_ThrowsArgumentNullException()
        {
            var jsonObject = new JObject();
            ImmutableHashSet<string> propertyNames = null;

            Assert.Throws<ArgumentNullException>(() => MetadataJsonHelper.SanitizeProperties(jsonObject, propertyNames));
        }

        [Fact]
        public void CreateJObjectWithSanitizedPropertyValue_ValidInput_SanitizesMatchingProperties()
        {
            var jsonObject = new JObject
            {
                { "sensitiveProperty1", "AccountKey=foo" },
                { "sensitiveProperty2", "MyConnection" },
                { "SENSITIVEPROPERTY3", "AccountKey=bar" },
                { "otherProperty", "value2" }
            };
            var sensitiveBindingPropertyNames = ImmutableHashSet.Create("sensitiveProperty1", "sensitiveproperty2", "sensitiveproperty3");

            var result = MetadataJsonHelper.SanitizeProperties(jsonObject, sensitiveBindingPropertyNames);

            Assert.Equal("[Hidden Credential]", result["sensitiveProperty1"].ToString());
            Assert.Equal("MyConnection", result["sensitiveProperty2"].ToString());
            Assert.Equal("[Hidden Credential]", result["SENSITIVEPROPERTY3"].ToString());
            Assert.Equal("value2", result["otherProperty"].ToString());
        }

        [Fact]
        public void CreateJObjectWithSanitizedPropertyValue_NoMatchingProperties_DoesNotSanitize()
        {
            var jsonObject = new JObject
            {
                { "otherProperty1", "value1" },
                { "otherProperty2", "value2" },
                { "otherProperty3", "AccountKey=foo" }
            };
            var sensitiveBindingPropertyNames = ImmutableHashSet.Create("sensitiveProperty");

            var result = MetadataJsonHelper.SanitizeProperties(jsonObject, sensitiveBindingPropertyNames);

            Assert.Equal("value1", result["otherProperty1"].ToString());
            Assert.Equal("value2", result["otherProperty2"].ToString());
            Assert.Equal("AccountKey=foo", result["otherProperty3"].ToString());
        }

        [Fact]
        public void CreateJObjectWithSanitizedPropertyValue_StringInput_NullOrEmptyJson_ThrowsArgumentException()
        {
            string json = null;
            var propertyNames = ImmutableHashSet.Create("sensitiveProperty");

            Assert.Throws<ArgumentException>(() => MetadataJsonHelper.CreateJObjectWithSanitizedPropertyValue(json, propertyNames));
        }

        [Fact]
        public void CreateJObjectWithSanitizedPropertyValue_StringInput_InvalidJson_ThrowsJsonReaderException()
        {
            var json = "invalid json";
            var propertyNames = ImmutableHashSet.Create("sensitiveProperty");

            Assert.Throws<Newtonsoft.Json.JsonReaderException>(() => MetadataJsonHelper.CreateJObjectWithSanitizedPropertyValue(json, propertyNames));
        }

        [Fact]
        public void CreateJObjectWithSanitizedPropertyValue_StringInput_ValidJson_SanitizesMatchingProperties()
        {
            var json = """{ "SensitiveProperty": "pwd=12345", "otherProperty": "value2" }""";
            var propertyNames = ImmutableHashSet.Create("sensitiveproperty");

            var result = MetadataJsonHelper.CreateJObjectWithSanitizedPropertyValue(json, propertyNames);

            Assert.Equal("[Hidden Credential]", result["SensitiveProperty"].ToString());
            Assert.Equal("value2", result["otherProperty"].ToString());
        }

        [Fact]
        public void CreateJObjectWithSanitizedPropertyValue_NullSensitiveProperty_DoesNotThrow()
        {
            var jsonObject = new JObject
            {
                { "connection", null },
                { "otherProperty1", "value1" },
                { "otherProperty2", string.Empty }
            };
            var propertyNames = ImmutableHashSet.Create("connection", "otherProperty2");

            var result = MetadataJsonHelper.SanitizeProperties(jsonObject, propertyNames);

            Assert.Equal(JTokenType.Null, result["connection"].Type); // Ensure null remains null
            Assert.Equal("value1", result["otherProperty1"].ToString());
            Assert.Equal(string.Empty, result["otherProperty2"].ToString()); // Ensure empty string remains empty
        }

        [Fact]
        public void CreateJObjectWithSanitizedPropertyValue_StringInput_DateTimeWithTimezoneOffset_RemainsUnchanged()
        {
            var json = """{ "timestamp": "2025-07-03T12:30:45+02:00", "otherProperty": "value2" }""";
            var propertyNames = ImmutableHashSet.Create("sensitiveProperty");

            var result = MetadataJsonHelper.CreateJObjectWithSanitizedPropertyValue(json, propertyNames);

            Assert.Equal("2025-07-03T12:30:45+02:00", result["timestamp"].ToObject<string>());  // ensure the value remains unchanged(not parsed as DateTime)
            Assert.Equal("value2", result["otherProperty"].ToString());
        }
    }
}