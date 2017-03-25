// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.ServiceBus.Messaging;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class NodeFunctionInvokerTests
    {
        [Theory]
        [InlineData(typeof(BlobType), true)]
        [InlineData(typeof(int), true)]
        [InlineData(typeof(int?), true)]
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
        [InlineData(typeof(Uri), true)]
        [InlineData(typeof(DateTime), true)]
        [InlineData(typeof(DateTime[]), true)]
        [InlineData(typeof(DateTimeOffset), true)]
        public void IsEdgeSupportedType_ReturnsExpectedResult(Type type, bool expected)
        {
            Assert.Equal(expected, NodeFunctionInvoker.IsEdgeSupportedType(type));
        }

        [Fact]
        public void NormalizeBindingData_TypeHandling()
        {
            var inputObject1 = new Dictionary<string, object>
            {
                { "TestProp1", "value1" },
                { "TestProp2", 123 }
            };
            var dateTime = DateTime.UtcNow;
            var inputObject2 = new Dictionary<string, object>
            {
                { "TestProp1", "value1" },
                { "TestProp2", 456 },
                { "TestProp3", new string[] { "value1", "value2", "value3" } },
                { "TestProp4", dateTime }
            };
            var objectArray = new Dictionary<string, object>[] { inputObject1, inputObject2 };
            var bindingData = new Dictionary<string, object>
            {
                { "TestProp1", "value1" },
                { "TestProp2", "value2" },
                { "TestProp3", inputObject1 },
                { "TestProp4", inputObject2 },
                { "TestProp5", objectArray },
                { "TestProp6", null },
                { "TestProp7", new BlobProperties { ContentType = "application/json", ContentMD5 = "xyz" } },
                { "TestProp8", new Uri("http://microsoft.com") },
                { "TestProp9", new PartitionContext { EventHubPath = "myhub", ConsumerGroupName = "Default" } }
            };
            var result = NodeFunctionInvoker.NormalizeBindingData(bindingData);

            Assert.Equal(9, result.Count);
            Assert.Equal(bindingData["TestProp1"], result["testProp1"]);
            Assert.Equal(bindingData["TestProp2"], result["testProp2"]);

            var blobProperties = (IDictionary<string, object>)result["testProp7"];
            Assert.Equal(16, blobProperties.Count);
            Assert.Equal("application/json", blobProperties["contentType"]);
            Assert.Equal("xyz", blobProperties["contentMD5"]);

            var partitionContextProperties = (IDictionary<string, object>)result["testProp9"];
            Assert.Equal(2, partitionContextProperties.Count);
            Assert.Equal("myhub", partitionContextProperties["eventHubPath"]);
            Assert.Equal("Default", partitionContextProperties["consumerGroupName"]);

            Assert.Equal("http://microsoft.com/", result["testProp8"].ToString());

            var resultChild = (IDictionary<string, object>)result["testProp3"];
            Assert.Equal(inputObject1["TestProp1"], resultChild["testProp1"]);
            Assert.Equal(inputObject1["TestProp2"], resultChild["testProp2"]);

            resultChild = (IDictionary<string, object>)result["testProp4"];
            Assert.Equal(inputObject2["TestProp1"], resultChild["testProp1"]);
            Assert.Equal(inputObject2["TestProp2"], resultChild["testProp2"]);
            var resultArray = (string[])resultChild["testProp3"];
            Assert.Equal(3, resultArray.Length);
            Assert.Equal("value1", resultArray[0]);
            Assert.Equal("value2", resultArray[1]);
            Assert.Equal("value3", resultArray[2]);
            Assert.Equal(dateTime, resultChild["testProp4"]);

            var resultObjectArray = (Dictionary<string, object>[])result["testProp5"];
            Assert.Equal(456, resultObjectArray[1]["testProp2"]);
        }

        [Fact]
        public void ToDictionary_ReturnsExpectedResult()
        {
            var test = new TestClass
            {
                Integer = 123,
                String = "Testing",
                Object = new TestClass()
            };
            var result = NodeFunctionInvoker.ToDictionary(test);

            Assert.Equal(test.Integer, result["integer"]);
            Assert.Equal(test.String, result["string"]);
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

        private class TestClass
        {
            public int Integer { get; set; }
            public string String { get; set; }
            public object Object { get; set; }

            public string Test()
            {
                return "Test!";
            }
        }
    }
}
