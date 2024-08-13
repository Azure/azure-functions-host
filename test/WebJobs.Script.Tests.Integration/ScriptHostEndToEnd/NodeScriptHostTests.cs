// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, nameof(NodeScriptHostTests))]
    public class NodeScriptHostTests : IClassFixture<NodeScriptHostTests.TestFixture>
    {
        private TestFixture Fixture;

        public NodeScriptHostTests(TestFixture fixture)
        {
            Fixture = fixture;
        }

        [Theory]
        [InlineData("httptrigger")]
        [InlineData("httptriggershared")]
        public async Task HttpTrigger_Get(string functionName)
        {
            string userAgent = "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/54.0.2840.71 Safari/537.36";
            string accept = "text/html, application/xhtml+xml, application/xml; q=0.9, */*; q=0.8";
            string customHeader = "foo,bar,baz";
            string url = $"http://localhost/api/{functionName}?name=Mathew%20Charles&location=Seattle";
            var request = HttpTestHelpers.CreateHttpRequest(
                "GET",
                url,
                new HeaderDictionary()
                {
                    ["test-header"] = "Test Request Header",
                    ["user-agent"] = userAgent,
                    ["accept"] = accept,
                    ["custom-1"] = customHeader
                });

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "request", request }
            };
            await Fixture.JobHost.CallAsync("HttpTrigger", arguments);

            var result = (IActionResult)request.HttpContext.Items[ScriptConstants.AzureFunctionsHttpResponseKey];

            Assert.IsType<RawScriptResult>(result);

            var objResult = result as RawScriptResult;

            Assert.Equal(200, objResult.StatusCode);

            Assert.Equal("Test Response Header", objResult.Headers["test-header"]);
            Assert.Equal("application/json; charset=utf-8", objResult.Headers["Content-Type"]);

            Assert.IsType<JObject>(objResult.Content);
            var resultObject = objResult.Content as JObject;
            Assert.Equal("undefined", (string)resultObject["reqBodyType"]);
            Assert.Null((string)resultObject["reqBody"]);
            Assert.Equal("undefined", (string)resultObject["reqRawBodyType"]);
            Assert.Null((string)resultObject["reqRawBody"]);

            // verify binding data was populated from query parameters
            Assert.Equal("Mathew Charles", (string)resultObject["bindingData"]["name"]);
            Assert.Equal("Seattle", (string)resultObject["bindingData"]["location"]);

            // validate input headers
            JObject reqHeaders = (JObject)resultObject["reqHeaders"];
            Assert.Equal("Test Request Header", reqHeaders["test-header"]);
            Assert.Equal(userAgent, reqHeaders["user-agent"]);
            Assert.Equal(accept, reqHeaders["accept"]);
            Assert.Equal(customHeader, reqHeaders["custom-1"]);

            // verify originalUrl is correct
            Assert.Equal(HttpUtility.UrlDecode(url), (string)resultObject["reqOriginalUrl"]);
        }

        [Theory]
        [InlineData("application/octet-stream")]
        [InlineData("multipart/form-data; boundary=----WebKitFormBoundaryTYtz7wze2XXrH26B")]
        public async Task HttpTrigger_Post_ByteArray(string contentType)
        {
            TestHelpers.ClearFunctionLogs("HttpTriggerByteArray");

            IHeaderDictionary headers = new HeaderDictionary();
            headers.Add("Content-Type", contentType);

            byte[] inputBytes = new byte[] { 1, 2, 3, 4, 5 };
            var content = inputBytes;

            HttpRequest request = HttpTestHelpers.CreateHttpRequest("POST", "http://localhost/api/httptriggerbytearray", headers, content);

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.JobHost.CallAsync("HttpTriggerByteArray", arguments);

            var result = (IActionResult)request.HttpContext.Items[ScriptConstants.AzureFunctionsHttpResponseKey];

            RawScriptResult objectResult = result as RawScriptResult;
            Assert.NotNull(objectResult);
            Assert.Equal(200, objectResult.StatusCode);

            JObject body = (JObject)objectResult.Content;
            Assert.True((bool)body["isBuffer"]);
            Assert.Equal(5, body["length"]);

            var rawBody = Encoding.UTF8.GetBytes((string)body["rawBody"]);
            Assert.Equal(inputBytes, rawBody);
        }

        [Fact]
        public async Task PromiseResolve()
        {
            JObject input = new JObject
            {
                { "scenario", "promiseResolve" }
            };

            Task t = Fixture.JobHost.CallAsync("Scenarios",
                new Dictionary<string, object>()
                {
                    { "input", input.ToString() }
                });

            Task result = await Task.WhenAny(t, Task.Delay(5000));
            Assert.Same(t, result);
            if (t.IsFaulted)
            {
                throw t.Exception;
            }
        }

        [Fact]
        public async Task PromiseApi_Resolves()
        {
            JObject input = new JObject
            {
                { "scenario", "promiseApiResolves" }
            };

            Task t = Fixture.JobHost.CallAsync("Scenarios",
                new Dictionary<string, object>()
                {
                    { "input", input.ToString() }
                });

            Task result = await Task.WhenAny(t, Task.Delay(5000));
            Assert.Same(t, result);
            if (t.IsFaulted)
            {
                throw t.Exception;
            }
        }

        [Fact]
        public async Task PromiseApi_Rejects()
        {
            JObject input = new JObject
            {
                { "scenario", "promiseApiRejects" }
            };

            Task t = Fixture.JobHost.CallAsync("Scenarios",
                new Dictionary<string, object>()
                {
                    { "input", input.ToString() }
                });

            Task result = await Task.WhenAny(t, Task.Delay(5000));
            Assert.Same(t, result);
            Assert.Equal(true, t.IsFaulted);
            Assert.Contains("reject", t.Exception.InnerException.InnerException.Message);
        }

        [Fact]
        public async Task ExecutionContext_IsProvided()
        {
            TestHelpers.ClearFunctionLogs("Scenarios");

            JObject input = new JObject
            {
                { "scenario", "functionExecutionContext" }
            };

            Task t = Fixture.JobHost.CallAsync("Scenarios",
                new Dictionary<string, object>()
                {
                    { "input", input.ToString() }
                });

            Task result = await Task.WhenAny(t, Task.Delay(5000));

            var logs = await TestHelpers.GetFunctionLogsAsync("Scenarios");

            Assert.Same(t, result);
            Assert.True(logs.Any(l => l.Contains("FunctionName:Scenarios")));
            Assert.True(logs.Any(l => l.Contains($"FunctionDirectory:{Path.Combine(Fixture.JobHost.ScriptOptions.RootScriptPath, "Scenarios")}")));
        }

        public class TestFixture : ScriptHostEndToEndTestFixture
        {
            static TestFixture()
            {
            }

            public TestFixture() : base(@"TestScripts\Node", "node", RpcWorkerConstants.NodeLanguageWorkerName,
                startHost: true, functions: new[] { "HttpTrigger", "Scenarios", "HttpTriggerByteArray" })
            {
            }

            protected override Task CreateTestStorageEntities()
            {
                // No need for this.
                return Task.CompletedTask;
            }
        }
    }
}
