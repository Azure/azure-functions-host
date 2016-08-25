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
        public static void ConvertBindingValue_PerformsExpectedConversions()
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
    }
}
