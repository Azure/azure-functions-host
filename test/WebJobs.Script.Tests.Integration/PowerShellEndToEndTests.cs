// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class PowerShellEndToEndTests : EndToEndTestsBase<PowerShellEndToEndTests.TestFixture>
    {
        public PowerShellEndToEndTests(TestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        public async Task ManualTrigger_Invoke_Succeeds()
        {
            await ManualTrigger_Invoke_SucceedsTest();
        }

        [Fact]
        public async Task QueueTriggerToBlob()
        {
            await QueueTriggerToBlobTest();
        }

        [Fact]
        public async Task BlobTriggerToBlobTest()
        {
            // the trigger blob was written by the fixture init code
            // here we just wait for the output blob
            CloudBlobContainer outputContainer = Fixture.BlobClient.GetContainerReference("test-output-powershell");
            var resultBlob = outputContainer.GetBlockBlobReference(Fixture.TestBlobName);
            await TestHelpers.WaitForBlobAsync(resultBlob);

            string resultContents = resultBlob.DownloadText();
            Assert.Equal(Fixture.TestBlobContents, resultContents.Trim());
        }

        [Fact]
        public async Task HttpTrigger_Get()
        {
            string testData = string.Format("Hello testuser");
            string testHeader = "TEST-HEADER";
            string testHeaderValue = "Test Request Header";
            string expectedResponseHeaderValue = "Test Response Header";
            string userAgent = "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/54.0.2840.71 Safari/537.36";
            string testHeader2 = "foo,bar,baz";

            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri("http://localhost/api/httptrigger?code=1388a6b0d05eca2237f10e4a4641260b0a08f3a5&name=testuser"),
                Method = HttpMethod.Get
            };
            request.SetConfiguration(new HttpConfiguration());
            request.Headers.Add(testHeader, testHeaderValue);
            request.Headers.Add("test-header", "Test Request Header");
            request.Headers.Add("User-Agent", userAgent);
            request.Headers.Add("custom-1", testHeader2);

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.Host.CallAsync("HttpTrigger", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string responseContent = await response.Content.ReadAsStringAsync();
            var result = JObject.Parse(responseContent);
            Assert.Equal(expectedResponseHeaderValue, (string)result["headers"][testHeader]);

            result = (JObject)result["result"];
            Assert.Equal(userAgent, (string)result["user-agent"]);
            Assert.Equal(testHeader2, (string)result["custom-1"]);
            Assert.Equal(testData, (string)result["message"]);
        }

        [Fact]
        public async Task HttpTrigger_CustomRoute()
        {
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri("http://localhost/api/products/produce/789?code=1388a6b0d05eca2237f10e4a4641260b0a08f3a5&name=testuser"),
                Method = HttpMethod.Get,
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            request.SetConfiguration(Fixture.RequestConfiguration);

            var routeData = new Dictionary<string, object>
            {
                { "category", "produce" },
                { "id", "789" }
            };
            request.Properties.Add(HttpExtensionConstants.AzureWebJobsHttpRouteDataKey, routeData);

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };

            await Fixture.Host.CallAsync("HttpTrigger-CustomRoute", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string result = await response.Content.ReadAsStringAsync();
            Assert.Equal("Name: testuser, Category: produce, Id:789", result);
        }

        [Fact]
        public async Task HttpTriggerWithError_Get()
        {
            string functionName = "HttpTrigger-WithError";
            TestHelpers.ClearFunctionLogs(functionName);

            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri =
                    new Uri(
                        "http://localhost/api/httptrigger-witherror?code=1388a6b0d05eca2237f10e4a4641260b0a08f3a5&name=testuser"),
                Method = HttpMethod.Get
            };
            request.SetConfiguration(new HttpConfiguration());

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };

            string innerException = "PowerShell script error";
            string errorRecordMessage = "Test Error in Get-DateToday";
            Exception ex = await
                Assert.ThrowsAsync<FunctionInvocationException>(async () => await Fixture.Host.CallAsync(functionName, arguments));
            var runtimeException = (RuntimeException)ex.InnerException;
            Assert.Equal(innerException, runtimeException.Message);
            Assert.Equal(errorRecordMessage, runtimeException.InnerException.Message);

            var logs = await TestHelpers.GetFunctionLogsAsync(functionName);
            Assert.True(logs.Any(p => p.Contains("Function started")));

            // verify an error was written
            Assert.True(logs.Any(p => p.Contains(errorRecordMessage)));

            // verify the function completed successfully
            Assert.True(logs.Any(p => p.Contains("Function completed (Failure")));
        }

        [Fact]
        public async Task HttpTrigger_Post_PlainText()
        {
            string testData = Guid.NewGuid().ToString();
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri("http://localhost/api/httptrigger?code=1388a6b0d05eca2237f10e4a4641260b0a08f3a5"),
                Method = HttpMethod.Post,
                Content = new StringContent(testData)
            };
            request.SetConfiguration(new HttpConfiguration());

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.Host.CallAsync("HttpTrigger", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string result = await response.Content.ReadAsStringAsync();
            JObject resultObject = JObject.Parse(result);
            Assert.Equal(testData, (string)resultObject["result"]["message"]["value"]);
            Assert.Equal("text/plain; charset=utf-8", (string)resultObject["result"]["content-type"]);
        }

        [Fact]
        public async Task HttpTrigger_Get_ExplicitResponse()
        {
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri("http://localhost/api/httptrigger-response?code=1388a6b0d05eca2237f10e4a4641260b0a08f3a5&name=testuser"),
                Method = HttpMethod.Get
            };
            request.SetConfiguration(new HttpConfiguration());

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.Host.CallAsync("HttpTrigger-Response", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string result = await response.Content.ReadAsStringAsync();
            Assert.Equal("text/html", response.Content.Headers.ContentType.MediaType);
            Assert.Equal("<HEAD><TITLE>Azure Functions!!!</TITLE></HEAD>", result);
        }

        [Fact]
        public async Task HttpTriggerExecutionContext_Get_ReturnsContextProperties()
        {
            string functionName = "HttpTrigger-ExecutionContext";
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri($"http://localhost/api/{functionName}?code=1388a6b0d05eca2237f10e4a4641260b0a08f3a5&name=testuser"),
                Method = HttpMethod.Get
            };
            request.SetConfiguration(new HttpConfiguration());

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.Host.CallAsync(functionName, arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string result = await response.Content.ReadAsStringAsync();
            string functionDirectory = Path.Combine(Fixture.Host.ScriptConfig.RootScriptPath, functionName);
            Assert.Equal($"FUNCTIONNAME={functionName},FUNCTIONDIRECTORY={functionDirectory}", result);
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture() : base(@"TestScripts\PowerShell", "powershell")
            {
            }

            public string TestBlobContents { get; private set; }

            public string TestBlobName { get; private set; }

            protected override void CreateTestStorageEntities()
            {
                // This will ensure the input container is created.
                base.CreateTestStorageEntities();

                TestBlobContents = "My Test Blob";
                TestBlobName = Guid.NewGuid().ToString();

                // write the test blob before the host starts, so it gets picked
                // up relatively quickly by the blob trigger test
                CloudBlockBlob inputBlob = TestInputContainer.GetBlockBlobReference(TestBlobName);
                inputBlob.UploadText(TestBlobContents);
            }
        }
    }
}
