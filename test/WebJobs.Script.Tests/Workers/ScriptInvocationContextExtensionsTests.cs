// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Google.Protobuf.Collections;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Workers.FunctionDataCache;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers
{
    public class ScriptInvocationContextExtensionsTests : IDisposable
    {
        private readonly ILoggerFactory _loggerFactory = MockNullLoggerFactory.CreateLoggerFactory();
        private readonly IEnvironment _testEnvironment;
        private readonly IMemoryMappedFileAccessor _mapAccessor;
        private readonly SharedMemoryManager _sharedMemoryManager;
        private readonly IFunctionDataCache _functionDataCache;

        public ScriptInvocationContextExtensionsTests()
        {
            ILogger<MemoryMappedFileAccessor> logger = NullLogger<MemoryMappedFileAccessor>.Instance;
            _testEnvironment = new TestEnvironment();
            _testEnvironment.SetEnvironmentVariable(FunctionDataCacheConstants.FunctionDataCacheEnabledSettingName, "1");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _mapAccessor = new MemoryMappedFileAccessorWindows(logger);
            }
            else
            {
                _mapAccessor = new MemoryMappedFileAccessorUnix(logger, _testEnvironment);
            }

            _sharedMemoryManager = new SharedMemoryManager(_loggerFactory, _mapAccessor);
            _functionDataCache = new FunctionDataCache(_sharedMemoryManager, _loggerFactory, _testEnvironment);
        }

        public void Dispose()
        {
            _sharedMemoryManager.Dispose();
            _functionDataCache.Dispose();
        }

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

            var inputs = new List<(string Name, DataType Type, object Val)>
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
                AsyncExecutionContext = System.Threading.ExecutionContext.Capture()
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
            capabilities.UpdateCapabilities(addedCapabilities, GrpcCapabilitiesUpdateStrategy.Merge);

            var result = await invocationContext.ToRpcInvocationRequest(logger, capabilities, isSharedMemoryDataTransferEnabled: false, _sharedMemoryManager);
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

            var inputs = new List<(string Name, DataType Type, object Val)>
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
                AsyncExecutionContext = System.Threading.ExecutionContext.Capture()
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
            var result = await invocationContext.ToRpcInvocationRequest(logger, capabilities, isSharedMemoryDataTransferEnabled: false, _sharedMemoryManager);
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

        [Fact]
        public async Task ToRpc_Http()
        {
            var rpcHttp = await CreateTestRpcHttp();

            Assert.Equal("http://local/test?a=b", rpcHttp.Url);
            Assert.Equal("test value", rpcHttp.Headers["test-header"]);
            Assert.Equal("b", rpcHttp.Query["a"]);
            Assert.Equal("test body", rpcHttp.Body.String);
        }

        [Fact]
        public async Task ToRpc_Http_WithProxy()
        {
            // Specify that we're using proxies.
            var rpcHttp = await CreateTestRpcHttp(new Dictionary<string, string>() { { "HttpUri", "something" } });

            // everything should come back empty
            Assert.Empty(rpcHttp.Url);
            Assert.Empty(rpcHttp.Headers);
            Assert.Empty(rpcHttp.Query);
            Assert.Null(rpcHttp.Body);
        }

        private async Task<RpcHttp> CreateTestRpcHttp(IDictionary<string, string> capabilities = null)
        {
            var logger = new TestLogger("test");
            GrpcCapabilities grpcCapabilities = new GrpcCapabilities(logger);
            if (capabilities is not null)
            {
                grpcCapabilities.UpdateCapabilities(capabilities, GrpcCapabilitiesUpdateStrategy.Merge);
            }

            var headers = new HeaderDictionary();
            headers.Add("test-header", "test value");
            var request = HttpTestHelpers.CreateHttpRequest("POST", "http://local/test?a=b", headers: headers, body: "test body");

            var bindingData = new Dictionary<string, object>
            {
                { "req", request },
            };

            var inputs = new List<(string Name, DataType Type, object Val)>
            {
                ("req", DataType.String, request),
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
                AsyncExecutionContext = System.Threading.ExecutionContext.Capture()
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

            functionMetadata.Bindings.Add(httpTriggerBinding);
            invocationContext.FunctionMetadata = functionMetadata;

            var result = await invocationContext.ToRpcInvocationRequest(logger, grpcCapabilities, isSharedMemoryDataTransferEnabled: false, _sharedMemoryManager);
            var resultHttp = result.InputData[0].Data.Http;
            Assert.Equal(1, result.TriggerMetadata.Count);
            Assert.Same(resultHttp, result.TriggerMetadata["req"].Http);

            Assert.Equal(1, result.InputData.Count);
            Assert.Equal("req", result.InputData[0].Name);

            return resultHttp;
        }

        [Fact]
        public void TestSetRetryContext_NoRetry()
        {
            ScriptInvocationContext context = new ScriptInvocationContext()
            {
                ExecutionContext = new ExecutionContext()
            };
            InvocationRequest request = new InvocationRequest();
            Grpc.ScriptInvocationContextExtensions.SetRetryContext(context, request);

            Assert.Null(request.RetryContext);
        }

        [Fact]
        public void TestSetRetryContext_Retry()
        {
            ScriptInvocationContext context = new ScriptInvocationContext()
            {
                ExecutionContext = new ExecutionContext()
                {
                    RetryContext = new Host.RetryContext()
                    {
                        RetryCount = 1,
                        MaxRetryCount = 2,
                        Exception = new Exception("test")
                    }
                }
            };
            InvocationRequest request = new InvocationRequest();
            Grpc.ScriptInvocationContextExtensions.SetRetryContext(context, request);

            Assert.NotNull(request.RetryContext);
            Assert.Equal(request.RetryContext.RetryCount, 1);
            Assert.Equal(request.RetryContext.MaxRetryCount, 2);
            Assert.NotNull(request.RetryContext.Exception);
        }

        /// <summary>
        /// The inputs meet the requirement for being transferred over shared memory.
        /// Ensure that the inputs are converted to <see cref="RpcSharedMemory"/>.
        /// </summary>
        [Fact]
        public async Task ToRpcInvocationRequest_RpcSharedMemoryDataTransfer()
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

            const int inputStringLength = 2 * 1024 * 1024;
            string inputString = TestUtils.GetRandomString(inputStringLength);

            const int inputBytesLength = 2 * 1024 * 1024;
            byte[] inputBytes = TestUtils.GetRandomBytesInArray(inputBytesLength);

            var inputs = new List<(string Name, DataType Type, object Val)>
            {
                ("req", DataType.String, httpContext.Request),
                ("fooStr", DataType.String, inputString),
                ("fooBytes", DataType.Binary, inputBytes),
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
                AsyncExecutionContext = System.Threading.ExecutionContext.Capture()
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

            var fooStrInputBinding = new BindingMetadata
            {
                Name = "fooStr",
                Type = "fooStr",
                Direction = BindingDirection.In
            };

            var fooBytesInputBinding = new BindingMetadata
            {
                Name = "fooBytes",
                Type = "fooBytes",
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
            functionMetadata.Bindings.Add(fooStrInputBinding);
            functionMetadata.Bindings.Add(fooBytesInputBinding);
            functionMetadata.Bindings.Add(httpOutputBinding);
            invocationContext.FunctionMetadata = functionMetadata;

            GrpcCapabilities capabilities = new GrpcCapabilities(logger);
            var result = await invocationContext.ToRpcInvocationRequest(logger, capabilities, isSharedMemoryDataTransferEnabled: true, _sharedMemoryManager);
            Assert.Equal(3, result.InputData.Count);

            Assert.Equal("fooStr", result.InputData[1].Name);
            Assert.Equal("fooBytes", result.InputData[2].Name);

            // The input data should be transferred over shared memory
            RpcSharedMemory sharedMem1 = result.InputData[1].RpcSharedMemory;

            // This is what the expected byte[] representation of the string should be
            // We use that to find expected length
            byte[] contentBytes = Encoding.UTF8.GetBytes(inputString);
            Assert.Equal(contentBytes.Length, sharedMem1.Count);

            // Check that the name of the shared memory map is a valid GUID
            Assert.True(Guid.TryParse(sharedMem1.Name, out _));

            // Check the type being sent
            Assert.Equal(sharedMem1.Type, RpcDataType.String);

            // The input data should be transferred over shared memory
            RpcSharedMemory sharedMem2 = result.InputData[2].RpcSharedMemory;

            Assert.Equal(inputBytes.Length, sharedMem2.Count);

            // Check that the name of the shared memory map is a valid GUID
            Assert.True(Guid.TryParse(sharedMem2.Name, out _));

            // Check the type being sent
            Assert.Equal(sharedMem2.Type, RpcDataType.Bytes);
        }

        /// <summary>
        /// The inputs meet the requirement for being transferred over shared memory.
        /// The inputs are provided in <see cref="ICacheAwareReadObject"/> type and are
        /// already present in shared memory (mocking the case where they are already cached in
        /// the <see cref="IFunctionDataCache"/>).
        /// Ensure that the inputs are converted to <see cref="RpcSharedMemory"/>.
        /// </summary>
        [Fact]
        public async Task ToRpcInvocationRequest_RpcSharedMemoryDataTransfer_UsingFunctionDataCache_CacheHit()
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

            const int inputStringLength = 2 * 1024 * 1024;
            string inputString = TestUtils.GetRandomString(inputStringLength);

            SharedMemoryMetadata sharedMemObj1 = await _sharedMemoryManager.PutObjectAsync(inputString);
            FunctionDataCacheKey key1 = new FunctionDataCacheKey("fooStr", "0x1");
            MockCacheAwareReadObject cacheObj1 = new MockCacheAwareReadObject(key1, sharedMemObj1, _functionDataCache);

            const int inputBytesLength = 2 * 1024 * 1024;
            byte[] inputBytes = TestUtils.GetRandomBytesInArray(inputBytesLength);

            SharedMemoryMetadata sharedMemObj2 = await _sharedMemoryManager.PutObjectAsync(inputBytes);
            FunctionDataCacheKey key2 = new FunctionDataCacheKey("fooBytes", "0x1");
            MockCacheAwareReadObject cacheObj2 = new MockCacheAwareReadObject(key2, sharedMemObj2, _functionDataCache);

            var inputs = new List<(string Name, DataType Type, object Val)>
            {
                ("req", DataType.String, httpContext.Request),
                ("fooStr", DataType.String, cacheObj1),
                ("fooBytes", DataType.Binary, cacheObj2),
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
                AsyncExecutionContext = System.Threading.ExecutionContext.Capture()
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

            var fooStrInputBinding = new BindingMetadata
            {
                Name = "fooStr",
                Type = "fooStr",
                Direction = BindingDirection.In
            };

            var fooBytesInputBinding = new BindingMetadata
            {
                Name = "fooBytes",
                Type = "fooBytes",
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
            functionMetadata.Bindings.Add(fooStrInputBinding);
            functionMetadata.Bindings.Add(fooBytesInputBinding);
            functionMetadata.Bindings.Add(httpOutputBinding);
            invocationContext.FunctionMetadata = functionMetadata;

            GrpcCapabilities capabilities = new GrpcCapabilities(logger);
            var result = await invocationContext.ToRpcInvocationRequest(logger, capabilities, isSharedMemoryDataTransferEnabled: true, _sharedMemoryManager);
            Assert.Equal(3, result.InputData.Count);

            Assert.Equal("fooStr", result.InputData[1].Name);
            Assert.Equal("fooBytes", result.InputData[2].Name);

            // The input data should be transferred over shared memory
            RpcSharedMemory sharedMem1 = result.InputData[1].RpcSharedMemory;

            // This is what the expected byte[] representation of the string should be
            // We use that to find expected length
            byte[] contentBytes = Encoding.UTF8.GetBytes(inputString);
            Assert.Equal(contentBytes.Length, sharedMem1.Count);

            // Check that the name of the shared memory map is a valid GUID
            Assert.True(Guid.TryParse(sharedMem1.Name, out _));

            // Check the type being sent
            Assert.Equal(sharedMem1.Type, RpcDataType.String);

            // The input data should be transferred over shared memory
            RpcSharedMemory sharedMem2 = result.InputData[2].RpcSharedMemory;

            Assert.Equal(inputBytes.Length, sharedMem2.Count);

            // Check that the name of the shared memory map is a valid GUID
            Assert.True(Guid.TryParse(sharedMem2.Name, out _));

            // Check the type being sent
            Assert.Equal(sharedMem2.Type, RpcDataType.Bytes);
        }

        /// <summary>
        /// The inputs meet the requirement for being transferred over shared memory.
        /// The inputs are provided in <see cref="ICacheAwareReadObject"/> type and are
        /// not already present in shared memory (mocking the case where they are not already
        /// cached in the <see cref="IFunctionDataCache"/>) but can be cached upon reading from
        /// storage.
        /// Ensure that the inputs are converted to <see cref="RpcSharedMemory"/>.
        /// </summary>
        [Fact]
        public async Task ToRpcInvocationRequest_RpcSharedMemoryDataTransfer_UsingFunctionDataCache_CacheMiss()
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

            const int inputStringLength = 2 * 1024 * 1024;
            string inputString = TestUtils.GetRandomString(inputStringLength);
            Stream inputStream1 = new MemoryStream();
            StreamWriter inputStreamWriter1 = new StreamWriter(inputStream1);
            await inputStreamWriter1.WriteAsync(inputString);
            await inputStreamWriter1.FlushAsync();
            inputStream1.Seek(0, SeekOrigin.Begin);

            FunctionDataCacheKey key1 = new FunctionDataCacheKey("fooStr", "0x1");
            MockCacheAwareReadObject cacheObj1 = new MockCacheAwareReadObject(key1, inputStream1, _functionDataCache);

            const int inputBytesLength = 2 * 1024 * 1024;
            byte[] inputBytes = TestUtils.GetRandomBytesInArray(inputBytesLength);
            Stream inputStream2 = new MemoryStream(inputBytes);
            inputStream2.Seek(0, SeekOrigin.Begin);

            FunctionDataCacheKey key2 = new FunctionDataCacheKey("fooBytes", "0x1");
            MockCacheAwareReadObject cacheObj2 = new MockCacheAwareReadObject(key2, inputStream2, _functionDataCache);

            var inputs = new List<(string Name, DataType Type, object Val)>
            {
                ("req", DataType.String, httpContext.Request),
                ("fooStr", DataType.String, cacheObj1),
                ("fooBytes", DataType.Binary, cacheObj2),
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
                AsyncExecutionContext = System.Threading.ExecutionContext.Capture()
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

            var fooStrInputBinding = new BindingMetadata
            {
                Name = "fooStr",
                Type = "fooStr",
                Direction = BindingDirection.In
            };

            var fooBytesInputBinding = new BindingMetadata
            {
                Name = "fooBytes",
                Type = "fooBytes",
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
            functionMetadata.Bindings.Add(fooStrInputBinding);
            functionMetadata.Bindings.Add(fooBytesInputBinding);
            functionMetadata.Bindings.Add(httpOutputBinding);
            invocationContext.FunctionMetadata = functionMetadata;

            GrpcCapabilities capabilities = new GrpcCapabilities(logger);
            var result = await invocationContext.ToRpcInvocationRequest(logger, capabilities, isSharedMemoryDataTransferEnabled: true, _sharedMemoryManager);
            Assert.Equal(3, result.InputData.Count);

            Assert.Equal("fooStr", result.InputData[1].Name);
            Assert.Equal("fooBytes", result.InputData[2].Name);

            // The input data should be transferred over shared memory
            RpcSharedMemory sharedMem1 = result.InputData[1].RpcSharedMemory;

            // This is what the expected byte[] representation of the string should be
            // We use that to find expected length
            byte[] contentBytes = Encoding.UTF8.GetBytes(inputString);
            Assert.Equal(contentBytes.Length, sharedMem1.Count);

            // Check that the name of the shared memory map is a valid GUID
            Assert.True(Guid.TryParse(sharedMem1.Name, out _));

            // Check the type being sent
            Assert.Equal(sharedMem1.Type, RpcDataType.String);

            // The input data should be transferred over shared memory
            RpcSharedMemory sharedMem2 = result.InputData[2].RpcSharedMemory;

            Assert.Equal(inputBytes.Length, sharedMem2.Count);

            // Check that the name of the shared memory map is a valid GUID
            Assert.True(Guid.TryParse(sharedMem2.Name, out _));

            // Check the type being sent
            Assert.Equal(sharedMem2.Type, RpcDataType.Bytes);

            // Check that the inputs were inserted into shared memory
            object inputStringReadObj = await _sharedMemoryManager.GetObjectAsync(sharedMem1.Name, 0, (int)sharedMem1.Count, typeof(string));
            Assert.NotNull(inputStringReadObj);
            string inputStringRead = inputStringReadObj as string;
            Assert.Equal(inputString, inputStringRead);

            object inputBytesReadObj = await _sharedMemoryManager.GetObjectAsync(sharedMem2.Name, 0, (int)sharedMem2.Count, typeof(byte[]));
            Assert.NotNull(inputBytesReadObj);
            byte[] inputBytesRead = inputBytesReadObj as byte[];
            Assert.Equal(inputBytes, inputBytesRead);

            // Check that the inputs were not marked to be removed after the invocation
            Assert.Empty(_sharedMemoryManager.InvocationSharedMemoryMaps);

            // Check that the inputs were inserted into the cache
            Assert.True(_functionDataCache.TryGet(key1, isIncrementActiveReference: false, out _));
            Assert.True(_functionDataCache.TryGet(key2, isIncrementActiveReference: false, out _));
        }

        /// <summary>
        /// The inputs don't meet the requirement for being transferred over shared memory.
        /// Ensure that the inputs are being shared over regular RPC.
        /// </summary>
        [Fact]
        public async Task ToRpcInvocationRequest_RpcDataTransfer()
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

            const int inputStringLength = 32;
            string inputString = TestUtils.GetRandomString(inputStringLength);

            const int inputBytesLength = 32;
            byte[] inputBytes = TestUtils.GetRandomBytesInArray(inputBytesLength);

            var inputs = new List<(string Name, DataType Type, object Val)>
            {
                ("req", DataType.String, httpContext.Request),
                ("fooStr", DataType.String, inputString),
                ("fooBytes", DataType.Binary, inputBytes),
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
                AsyncExecutionContext = System.Threading.ExecutionContext.Capture()
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

            var fooStrInputBinding = new BindingMetadata
            {
                Name = "fooStr",
                Type = "fooStr",
                Direction = BindingDirection.In
            };

            var fooBytesInputBinding = new BindingMetadata
            {
                Name = "fooBytes",
                Type = "fooBytes",
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
            functionMetadata.Bindings.Add(fooStrInputBinding);
            functionMetadata.Bindings.Add(fooBytesInputBinding);
            functionMetadata.Bindings.Add(httpOutputBinding);
            invocationContext.FunctionMetadata = functionMetadata;

            GrpcCapabilities capabilities = new GrpcCapabilities(logger);
            var result = await invocationContext.ToRpcInvocationRequest(logger, capabilities, isSharedMemoryDataTransferEnabled: true, _sharedMemoryManager);
            Assert.Equal(3, result.InputData.Count);

            Assert.Equal("fooStr", result.InputData[1].Name);
            Assert.Equal("fooBytes", result.InputData[2].Name);

            // The input data should be transferred over regular RPC despite enabling shared memory
            Assert.Equal(inputString, result.InputData[1].Data.String);
            Assert.Equal(inputBytes, result.InputData[2].Data.Bytes);
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
