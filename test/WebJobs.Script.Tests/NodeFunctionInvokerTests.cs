// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class NodeFunctionInvokerTests
    {
        [Fact]
        public void ConvertBindingValue_PerformsExpectedConversions()
        {
            var c = new ExpandoObject() as IDictionary<string, object>;
            c["A"] = "Testing";
            c["B"] = 1234;
            c["C"] = new object[] { 1, "Two", 3 };

            // don't expect functions to be serialized
            c["D"] = (Func<object, Task<object>>)(p => { return Task.FromResult<object>(null); });

            var o = new ExpandoObject() as IDictionary<string, object>;
            o["A"] = "Testing";
            o["B"] = c;

            string json = (string)NodeFunctionInvoker.ConvertBindingValue(o);
            Assert.Equal("{\"A\":\"Testing\",\"B\":{\"A\":\"Testing\",\"B\":1234,\"C\":[1,\"Two\",3]}}", json.Replace(" ", string.Empty));
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
            Dictionary<string, object> obj = (Dictionary<string, object>)objects[0];
            Assert.Equal("Larry", obj["name"]);

            obj = (Dictionary<string, object>)objects[1];
            Assert.Equal("Moe", obj["name"]);

            obj = (Dictionary<string, object>)objects[2];
            Assert.Equal("Curly", obj["name"]);
        }
    }
}
