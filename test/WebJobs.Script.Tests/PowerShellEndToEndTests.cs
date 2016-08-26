// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
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

            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri("http://localhost/api/httptrigger-powershell?code=1388a6b0d05eca2237f10e4a4641260b0a08f3a5&name=testuser"),
                Method = HttpMethod.Get
            };
            request.SetConfiguration(new HttpConfiguration());
            request.Headers.Add(testHeader, testHeaderValue);

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.Host.CallAsync("HttpTrigger-PowerShell", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string result = await response.Content.ReadAsStringAsync();
            JObject resultObject = JObject.Parse(result);
            Assert.Equal(expectedResponseHeaderValue, (string)resultObject["headers"][testHeader]);
            Assert.Equal(testData, (string)resultObject["reqBody"]);
        }

        [Fact]
        public async Task HttpTriggerWithError_Get()
        {
            string functionName = "HttpTrigger-PowerShellWithError";
            TestHelpers.ClearFunctionLogs(functionName);

            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri =
                    new Uri(
                        "http://localhost/api/httptrigger-powershellwitherror?code=1388a6b0d05eca2237f10e4a4641260b0a08f3a5&name=testuser"),
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
                RequestUri = new Uri("http://localhost/api/httptrigger-powershell?code=1388a6b0d05eca2237f10e4a4641260b0a08f3a5"),
                Method = HttpMethod.Post,
                Content = new StringContent(testData)
            };
            request.SetConfiguration(new HttpConfiguration());

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.Host.CallAsync("HttpTrigger-PowerShell", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string result = await response.Content.ReadAsStringAsync();
            JObject resultObject = JObject.Parse(result);
            Assert.Equal(testData, (string)resultObject["reqBody"]["value"]);
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
