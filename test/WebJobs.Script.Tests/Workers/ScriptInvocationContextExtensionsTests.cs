// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Protobuf.Collections;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers
{
    public class ScriptInvocationContextExtensionsTests
    {
        [Theory]
        [InlineData("someTraceParent", "someTraceState", null)]
        [InlineData("", "", null)]
        [InlineData(null, null, null)]
        public void TestGetRpcTraceContext_WithExpectedValues(string traceparent, string tracestate, IEnumerable<KeyValuePair<string, string>> attributes)
        {
            IEnumerable<KeyValuePair<string, string>> expectedAttributes = null;
            if (!string.IsNullOrEmpty(traceparent))
            {
                attributes = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("key1", "value1"), new KeyValuePair<string, string>("key1", "value2") };
                expectedAttributes = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("key1", "value2") };
            }

            RpcTraceContext traceContext = Grpc.ScriptInvocationContextExtensions.GetRpcTraceContext(traceparent, tracestate, attributes, NullLogger.Instance);

            Assert.Equal(traceparent ?? string.Empty, traceContext.TraceParent);
            Assert.Equal(tracestate ?? string.Empty, traceContext.TraceState);

            if (attributes != null)
            {
                Assert.True(expectedAttributes.SequenceEqual(traceContext.Attributes));
            }
            else
            {
                Assert.Equal(0, traceContext.Attributes.Count);
            }
        }

        [Fact]
        public void GetHttpScriptInvocationContextValueTest_String()
        {
            string inputValue = "stringTest";
            object result = Script.Workers.ScriptInvocationContextExtensions.GetHttpScriptInvocationContextValue(inputValue);
            Assert.Equal($"\"{inputValue}\"", result);
        }

        [Fact]
        public void GetHttpScriptInvocationContextValueTest_POCO()
        {
            TestPoco inputValue = new TestPoco()
            {
                Name = "TestName",
                Id = 1234
            };
            object result = Script.Workers.ScriptInvocationContextExtensions.GetHttpScriptInvocationContextValue(inputValue);
            var resultAsJObject = (JObject)result;
            Assert.Equal("TestName", resultAsJObject["Name"]);
            Assert.Equal(1234, resultAsJObject["Id"]);
        }

        [Fact]
        public async Task ToRpcInvocationRequest_Http_OmitsDuplicateBodyOfBindingData()
        {
            var logger = new TestLogger("test");

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Host = new HostString("local");
            httpContext.Request.Path = "/test";
            httpContext.Request.Method = "Post";

            var inputs = new List<(string name, DataType type, object val)>
            {
                ("req", DataType.String, httpContext.Request)
            };

            var bindingData = new Dictionary<string, object>
            {
                { "req", httpContext.Request },
                { "$request", httpContext.Request },
                { "headers", httpContext.Request.Headers.ToDictionary(p => p.Key, p => p.Value) },
                { "query", httpContext.Request.QueryString.ToString() },
                { "sys", new SystemBindingData() }
            };

            var invocationContext = new ScriptInvocationContext()
            {
                ExecutionContext = new ExecutionContext()
                {
                    InvocationId = Guid.NewGuid(),
                    FunctionName = "Test",
                },
                BindingData = bindingData,
                Inputs = inputs,
                ResultSource = new TaskCompletionSource<ScriptInvocationResult>(),
                Logger = logger,
                AsyncExecutionContext = System.Threading.ExecutionContext.Capture(),
                SharedMemoryResources = new List<string>()
            };

            var functionMetadata = new FunctionMetadata
            {
                Name = "Test"
            };

            var httpTriggerBinding = new BindingMetadata
            {
                Name = "req",
                Type = "httpTrigger",
                Direction = BindingDirection.In,
                Raw = new JObject()
            };

            var httpOutputBinding = new BindingMetadata
            {
                Name = "res",
                Type = "http",
                Direction = BindingDirection.Out,
                Raw = new JObject(),
                DataType = DataType.String
            };

            functionMetadata.Bindings.Add(httpTriggerBinding);
            functionMetadata.Bindings.Add(httpOutputBinding);
            invocationContext.FunctionMetadata = functionMetadata;

            GrpcCapabilities capabilities = new GrpcCapabilities(logger);
            MapField<string, string> addedCapabilities = new MapField<string, string>
            {
                { RpcWorkerConstants.RpcHttpTriggerMetadataRemoved, "1" },
                { RpcWorkerConstants.RpcHttpBodyOnly, "1" }
            };
            capabilities.UpdateCapabilities(addedCapabilities);

            var result = await invocationContext.ToRpcInvocationRequest(logger, capabilities);
            Assert.Equal(1, result.InputData.Count);
            Assert.Equal(2, result.TriggerMetadata.Count);
            Assert.True(result.TriggerMetadata.ContainsKey("headers"));
            Assert.True(result.TriggerMetadata.ContainsKey("query"));
        }

        [Fact]
        public async Task ToRpcInvocationRequest_MultipleInputBindings()
        {
            var logger = new TestLogger("test");

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Host = new HostString("local");
            httpContext.Request.Path = "/test";
            httpContext.Request.Method = "Post";

            var poco = new TestPoco { Id = 1, Name = "Test" };

            var bindingData = new Dictionary<string, object>
            {
                { "req", httpContext.Request },
                { "$request", httpContext.Request },
                { "headers", httpContext.Request.Headers.ToDictionary(p => p.Key, p => p.Value) },
                { "query", httpContext.Request.QueryString.ToString() },
                { "sys", new SystemBindingData() }
            };

            var inputs = new List<(string name, DataType type, object val)>
            {
                ("req", DataType.String, httpContext.Request),
                ("blob", DataType.String, null),  // verify that null values are handled
                ("foo", DataType.String, "test"),
                ("bar1", DataType.String, poco),
                ("bar2", DataType.String, poco)
            };

            var invocationContext = new ScriptInvocationContext()
            {
                ExecutionContext = new ExecutionContext()
                {
                    InvocationId = Guid.NewGuid(),
                    FunctionName = "Test",
                },
                BindingData = bindingData,
                Inputs = inputs,
                ResultSource = new TaskCompletionSource<ScriptInvocationResult>(),
                Logger = logger,
                AsyncExecutionContext = System.Threading.ExecutionContext.Capture(),
                SharedMemoryResources = new List<string>()
            };

            var functionMetadata = new FunctionMetadata
            {
                Name = "Test"
            };

            var httpTriggerBinding = new BindingMetadata
            {
                Name = "req",
                Type = "httpTrigger",
                Direction = BindingDirection.In,
                Raw = new JObject()
            };

            var blobInputBinding = new BindingMetadata
            {
                Name = "blob",
                Type = "blob",
                Direction = BindingDirection.In
            };

            var fooInputBinding = new BindingMetadata
            {
                Name = "foo",
                Type = "foo",
                Direction = BindingDirection.In
            };

            var barInputBinding1 = new BindingMetadata
            {
                Name = "bar1",
                Type = "bar",
                Direction = BindingDirection.In
            };

            var barInputBinding2 = new BindingMetadata
            {
                Name = "bar2",
                Type = "bar",
                Direction = BindingDirection.In
            };

            var httpOutputBinding = new BindingMetadata
            {
                Name = "res",
                Type = "http",
                Direction = BindingDirection.Out,
                Raw = new JObject(),
                DataType = DataType.String
            };

            functionMetadata.Bindings.Add(httpTriggerBinding);
            functionMetadata.Bindings.Add(blobInputBinding);
            functionMetadata.Bindings.Add(fooInputBinding);
            functionMetadata.Bindings.Add(barInputBinding1);
            functionMetadata.Bindings.Add(barInputBinding2);
            functionMetadata.Bindings.Add(httpOutputBinding);
            invocationContext.FunctionMetadata = functionMetadata;

            GrpcCapabilities capabilities = new GrpcCapabilities(logger);
            var result = await invocationContext.ToRpcInvocationRequest(logger, capabilities);
            Assert.Equal(5, result.InputData.Count);

            Assert.Equal("req", result.InputData[0].Name);
            var resultHttp = result.InputData[0].Data;
            Assert.Equal("http://local/test", ((RpcHttp)result.InputData[0].Data.Http).Url);

            // verify the null input was propagated properly
            Assert.Equal("blob", result.InputData[1].Name);
            Assert.Equal(string.Empty, result.InputData[1].Data.String);

            Assert.Equal("foo", result.InputData[2].Name);
            Assert.Equal("test", result.InputData[2].Data.String);

            Assert.Equal("bar1", result.InputData[3].Name);
            var resultPoco = result.InputData[3].Data;
            Assert.Equal("{\"Name\":\"Test\",\"Id\":1}", resultPoco.Json);

            Assert.Equal("bar2", result.InputData[4].Name);
            Assert.Same(resultPoco, result.InputData[4].Data);

            Assert.Equal(4, result.TriggerMetadata.Count);
            Assert.Same(resultHttp, result.TriggerMetadata["req"]);
            Assert.Same(resultHttp, result.TriggerMetadata["$request"]);
            Assert.True(result.TriggerMetadata.ContainsKey("headers"));
            Assert.True(result.TriggerMetadata.ContainsKey("query"));
        }

        private class TestPoco
        {
            public string Name { get; set; }

            public int Id { get; set; }
        }

        private class SystemBindingData
        {
        }
    }
}
