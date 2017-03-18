// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Description;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class NodeFunctionInvokerTests
    {
        [Theory]
        [InlineData(typeof(int), true)]
        [InlineData(typeof(double), true)]
        [InlineData(typeof(string), true)]
        [InlineData(typeof(bool), true)]
        [InlineData(typeof(byte), true)]
        [InlineData(typeof(object), true)]
        [InlineData(typeof(int[]), true)]
        [InlineData(typeof(double[]), true)]
        [InlineData(typeof(string[]), true)]
        [InlineData(typeof(bool[]), true)]
        [InlineData(typeof(byte[]), true)]
        [InlineData(typeof(object[]), true)]
        public void IsEdgeSupportedType_ReturnsExpectedResult(Type type, bool expected)
        {
            Assert.Equal(expected, NodeFunctionInvoker.IsEdgeSupportedType(type));
        }

        [Fact]
        public void NormalizeBindingData_TypeHandling()
        {
            var nested1 = new Dictionary<string, object>
            {
                { "TestProp1", "value1" },
                { "TestProp2", 123 }
            };
            var nested2 = new Dictionary<string, object>
            {
                { "TestProp1", "value1" },
                { "TestProp2", 456 },
                { "TestProp3", new string[] { "value1", "value2", "value3" } }
            };
            var bindingData = new Dictionary<string, object>
            {
                { "TestProp1", "value1" },
                { "TestProp2", "value2" },
                { "TestProp3", nested1 },
                { "TestProp4", nested2 }
            };
            var result = NodeFunctionInvoker.NormalizeBindingData(bindingData);

            Assert.Equal(4, bindingData.Count);
            Assert.Equal(bindingData["TestProp1"], result["testProp1"]);
            Assert.Equal(bindingData["TestProp2"], result["testProp2"]);

            var resultChild = (IDictionary<string, object>)result["testProp3"];
            Assert.Equal(nested1["TestProp1"], resultChild["testProp1"]);
            Assert.Equal(nested1["TestProp2"], resultChild["testProp2"]);

            resultChild = (IDictionary<string, object>)result["testProp4"];
            Assert.Equal(nested2["TestProp1"], resultChild["testProp1"]);
            Assert.Equal(nested2["TestProp2"], resultChild["testProp2"]);

            var arrayValue = (string[])resultChild["testProp3"];
            Assert.Equal(3, arrayValue.Length);
            Assert.Equal("value1", arrayValue[0]);
            Assert.Equal("value2", arrayValue[1]);
            Assert.Equal("value3", arrayValue[2]);
        }

        [Fact]
        public void TryConvertJson_ArrayOfJsonObjectStrings()
        {
            string[] input = new string[]
            {
                "{ \"name\": \"Larry\" }",
                "{ \"name\": \"Moe\" }",
                "{ \"name\": \"Curly\" }"
            };

            object result = null;
            bool didConvert = NodeFunctionInvoker.TryConvertJson(input, out result);
            Assert.True(didConvert);

            object[] objects = result as object[];
            IDictionary<string, object> obj = (IDictionary<string, object>)objects[0];
            Assert.Equal("Larry", obj["name"]);

            obj = (IDictionary<string, object>)objects[1];
            Assert.Equal("Moe", obj["name"]);

            obj = (IDictionary<string, object>)objects[2];
            Assert.Equal("Curly", obj["name"]);
        }

        [Fact]
        public void TryConvertJson_JsonObjectString()
        {
            JObject child = new JObject
            {
                { "Name", "Mary" },
                { "Location", "Seattle" },
                { "Age", 5 }
            };

            JObject parent = new JObject
            {
                { "Name", "Bob" },
                { "Location", "Seattle" },
                { "Age", 40 },
                { "Children", new JArray(child) }
            };

            object result;
            string input = parent.ToString();
            bool didConvert = NodeFunctionInvoker.TryConvertJson(input, out result);
            Assert.True(didConvert);

            var resultDictionary = (IDictionary<string, object>)result;
            var resultChildren = (IEnumerable<object>)resultDictionary["Children"];
            var resultChild = (IDictionary<string, object>)resultChildren.ElementAt(0);
            Assert.Equal(5, (long)resultChild["Age"]);
        }
    }
}
