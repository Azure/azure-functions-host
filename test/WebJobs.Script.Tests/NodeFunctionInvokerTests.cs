// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Description;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class NodeFunctionInvokerTests
    {
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
