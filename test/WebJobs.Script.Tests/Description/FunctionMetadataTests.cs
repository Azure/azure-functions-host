// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Description;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionMetadataTests
    {
        public static IEnumerable<object[]> HttpInAndOutFunctionMetadata
        {
            get
            {
                yield return new object[] { GetTestFunctionMetadata("test1", addHttpOut: true) };
                yield return new object[] { GetTestFunctionMetadata("test1", addHttpReturn: true) };
            }
        }

        public static IEnumerable<object[]> TestFunctionMetadata
        {
            get
            {
                yield return new object[] { GetTestFunctionMetadata("test1", addHttpTriggerBinding: false) };
                yield return new object[] { GetTestFunctionMetadata("test1", addHttpOut: false) };
                yield return new object[] { GetTestFunctionMetadata("test1", addQueueInput: true) };
                yield return new object[] { GetTestFunctionMetadata("test1", addHttpTriggerBinding: false, addQueueInput: true, addQueueOutput: true) };
                yield return new object[] { GetTestFunctionMetadata("test1", addHttpTriggerBinding: false, addHttpReturn: true, addQueueOutput: true) };
            }
        }

        [Theory]
        [MemberData(nameof(HttpInAndOutFunctionMetadata))]
        public void IsSimpleHttpTriggerFunction_Returns_True(FunctionMetadata functionMetadata)
        {
            Assert.True(functionMetadata.IsHttpInAndOutFunction);
        }

        [Theory]
        [MemberData(nameof(TestFunctionMetadata))]
        public void IsSimpleHttpTriggerFunction_Returns_False(FunctionMetadata functionMetadata)
        {
            Assert.False(functionMetadata.IsHttpInAndOutFunction);
        }

        public static FunctionMetadata GetTestFunctionMetadata(string name, bool addHttpTriggerBinding = true, bool addQueueInput = false, bool addQueueOutput = false, bool addHttpOut = false, bool addHttpReturn = false)
        {
            var functionMetadata = new FunctionMetadata
            {
                Name = name
            };
            var httpTriggerBinding = new BindingMetadata
            {
                Name = "req",
                Type = "httpTrigger",
                Direction = BindingDirection.In,
                Raw = new JObject()
            };
            var queueInputBinding = new BindingMetadata
            {
                Name = "queueInput",
                Type = "queue",
                Direction = BindingDirection.In
            };
            var httpOutputBinding = new BindingMetadata
            {
                Name = "res",
                Type = "http",
                Direction = BindingDirection.Out,
                Raw = new JObject()
            };
            var httpReturnOutputBinding = new BindingMetadata
            {
                Name = "$return",
                Type = "http",
                Direction = BindingDirection.Out,
                Raw = new JObject()
            };
            var queueReturnOutputBinding = new BindingMetadata
            {
                Name = "$return",
                Type = "queue",
                Direction = BindingDirection.Out,
                Raw = new JObject()
            };

            if (addHttpTriggerBinding)
            {
                functionMetadata.Bindings.Add(httpTriggerBinding);
            }
            if (addHttpOut)
            {
                functionMetadata.Bindings.Add(httpOutputBinding);
            }
            if (addHttpReturn)
            {
                functionMetadata.Bindings.Add(httpReturnOutputBinding);
            }
            if (addQueueInput)
            {
                functionMetadata.Bindings.Add(queueInputBinding);
            }
            if (addQueueOutput)
            {
                functionMetadata.Bindings.Add(queueReturnOutputBinding);
            }
            return functionMetadata;
        }
    }
}
