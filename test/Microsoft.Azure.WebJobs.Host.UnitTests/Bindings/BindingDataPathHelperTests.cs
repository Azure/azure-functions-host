// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;
using Xunit.Extensions;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Bindings
{
    public class BindingDataPathHelperTests
    {
        [Theory]
        [InlineData(typeof(Int16), "-123")]
        [InlineData(typeof(Int32), "1234567890")]
        [InlineData(typeof(Int64), "123000000000000000")]
        [InlineData(typeof(UInt16), "123")]
        [InlineData(typeof(UInt32), "1234567890")]
        [InlineData(typeof(UInt64), "1230000000000000000")]
        [InlineData(typeof(Char), "R")]
        [InlineData(typeof(Byte), "255")]
        [InlineData(typeof(SByte), "-127")]
        public void ConvertParamValueToString_IfSupportedType_ReturnsStringValue(Type paramType, string expectedStringValue)
        {
            // Arrange
            var parseMethod = paramType.GetMethod("Parse", new Type[] { typeof(string) });
            object paramValue = parseMethod.Invoke(null, new object[] { expectedStringValue });

            // Act
            string stringParamValue = BindingDataPathHelper.ConvertParameterValueToString(paramValue);

            // Assert
            Assert.NotNull(stringParamValue);
            Assert.Equal(expectedStringValue, stringParamValue);
        }

        [Fact]
        public void ConvertParamValueToString_IfStringParam_ReturnsStringValue()
        {
            // Arrange
            const string expectedStringValue = "Some random test string";

            // Act
            string stringParamValue = BindingDataPathHelper.ConvertParameterValueToString(expectedStringValue);

            // Assert
            Assert.NotNull(stringParamValue);
            Assert.Equal(expectedStringValue, stringParamValue);
        }

        [Fact]
        public void ConvertParamValueToString_IfGuidParam_ReturnsStringValue()
        {
            // Arrange
            string expectedStringValue = "c914be08-fae6-4014-a619-c5f7ebf3fe37";
            Guid guidParam = Guid.Parse(expectedStringValue);

            // Act
            string stringParamValue = BindingDataPathHelper.ConvertParameterValueToString(guidParam);

            // Assert
            Assert.NotNull(stringParamValue);
            Assert.Equal(expectedStringValue, stringParamValue);
        }

        [Fact]
        public void ConvertParamValueToString_IfUnupportedType_ReturnsNull()
        {
            // Arrange
            DateTime dateTimeParam = DateTime.Now;

            // Act
            string stringParamValue = BindingDataPathHelper.ConvertParameterValueToString(dateTimeParam);

            // Assert
            Assert.Null(stringParamValue);
        }

        // Test various JObject conversions. 
        [Fact]
        public void ConvertParamValueToString_IfJTokenParam_ReturnsStringValue()
        {
            Guid guid = new Guid("c914be08fae64014a619c5f7ebf3fe37");
            var date = DateTime.UtcNow;

            Test(date.ToString(), JToken.FromObject(date));
            Test("123", JToken.FromObject(123));
            Test("str", JToken.FromObject("str"));
            Test("123.45", JToken.FromObject(123.45));

            // Guids are normalized
            Test("c914be08-fae6-4014-a619-c5f7ebf3fe37", JToken.FromObject(guid)); 

            // Complex types are null, just like System.Object
            Test(null, new JObject());
            Test(null, new JArray());
        }            

        private static void Test(string expectedStringValue, JToken obj)
        {
            // Act
            string stringParamValue = BindingDataPathHelper.ConvertParameterValueToString(obj);

            // Assert
            Assert.Equal(expectedStringValue, stringParamValue);
        }        
    }
}
