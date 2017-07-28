// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    [Trait("Category", "E2E")]
    [Trait("E2E", nameof(WindowsBatchEndToEndTests))]
    public class WindowsBatchEndToEndTests : EndToEndTestsBase<WindowsBatchEndToEndTests.TestFixture>
    {
        public WindowsBatchEndToEndTests(TestFixture fixture)
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
        public async Task WebHookTrigger_GenericJson()
        {
            string testData = Guid.NewGuid().ToString();
            JObject testObject = new JObject
            {
                { "a", testData }
            };
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/webhooktrigger?code=1388a6b0d05eca2237f10e4a4641260b0a08f3a5")),
                Method = HttpMethod.Post,
                Content = new StringContent(testObject.ToString(Formatting.None))
            };
            request.SetConfiguration(Fixture.RequestConfiguration);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.Host.CallAsync("WebHookTrigger", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync();
            string expected = string.Format("WebHook processed successfully! {0}", testObject.ToString(Formatting.None)).Trim();
            Assert.Equal(expected, body.Trim());
        }

        [Fact]
        public async Task HttpTrigger_Get()
        {
            string testData = Guid.NewGuid().ToString();
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri($"http://localhost/api/httptrigger?value={testData}&a=one&a=two&b=three&details=1"),
                Method = HttpMethod.Get
            };
            request.SetConfiguration(Fixture.RequestConfiguration);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            request.Headers.Add("test-header", "Test Request Header");

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.Host.CallAsync("HttpTrigger", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync();
            string[] lines = body.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal($"URL = \"http://localhost/api/httptrigger?value={testData}&a=one&a=two&b=three&details=1\"", lines[0].Trim());
            Assert.Equal($"Query = \"?value={testData}&a=one&a=two&b=three&details=1\"", lines[1].Trim());
            Assert.Equal("test-header = Test Request Header", lines[2].Trim());
            Assert.Equal($"Value = {testData}", lines[3].Trim());

            request.RequestUri = new Uri(string.Format("http://localhost/api/httptrigger", testData));
            request.Headers.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            await Fixture.Host.CallAsync("HttpTrigger", arguments);
            response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            body = await response.Content.ReadAsStringAsync();
            Assert.Equal("Please pass a value on the query string", body.Trim());
        }

        [Fact]
        public async Task HttpTrigger_Post_String()
        {
            string testData = Guid.NewGuid().ToString();
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri("http://localhost/api/httptrigger"),
                Method = HttpMethod.Post,
                Content = new StringContent(testData)
            };
            request.SetConfiguration(Fixture.RequestConfiguration);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.Host.CallAsync("HttpTrigger", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync();
            string[] lines = body.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(string.Format("Body = {0}", testData), lines[0].Trim());
            Assert.Equal("Please pass a value on the query string", lines[1].Trim());
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture() : base(@"TestScripts\WindowsBatch", "batch")
            {
            }
        }
    }
}
