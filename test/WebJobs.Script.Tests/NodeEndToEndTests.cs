// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;

namespace WebJobs.Script.Tests
{
    public class NodeEndToEndTests : EndToEndTestsBase<NodeEndToEndTests.TestFixture>
    {
        public NodeEndToEndTests(TestFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task ManualTrigger()
        {
            string testData = Guid.NewGuid().ToString();
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "input", testData }
            };
            await Fixture.Host.CallAsync("ManualTrigger", arguments);
        }

        [Fact]
        public async Task HttpTrigger_Get()
        {
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/httptrigger")),
                Method = HttpMethod.Get,
            };

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.Host.CallAsync("HttpTrigger", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties["MS_AzureFunctionsHttpResponse"];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync();
            JObject resultObject = JObject.Parse(body);
            Assert.Equal((string)resultObject["type"], "undefined");
            Assert.Null((string)resultObject["body"]);
        }

        [Fact]
        public async Task HttpTrigger_Post_PlainText()
        {
            string testData = Guid.NewGuid().ToString();
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/httptrigger")),
                Method = HttpMethod.Post,
                Content = new StringContent(testData)
            };

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.Host.CallAsync("HttpTrigger", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties["MS_AzureFunctionsHttpResponse"];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync();
            JObject resultObject = JObject.Parse(body);
            Assert.Equal((string)resultObject["type"], "string");
            Assert.Equal((string)resultObject["body"], testData);
        }

        [Fact]
        public async Task HttpTrigger_Post_Json()
        {
            string testData = Guid.NewGuid().ToString();
            JObject testObject = new JObject
            {
                { "testData", testData }
            };
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/httptrigger")),
                Method = HttpMethod.Post,
                Content = new StringContent(testObject.ToString())
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.Host.CallAsync("HttpTrigger", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties["MS_AzureFunctionsHttpResponse"];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync();
            JObject resultObject = JObject.Parse(body);
            Assert.Equal((string)resultObject["type"], "object");
            Assert.Equal((string)resultObject["body"]["testData"], testData);
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
                Content = new StringContent(testObject.ToString())
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.Host.CallAsync("WebHookTrigger", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties["MS_AzureFunctionsHttpResponse"];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal(string.Format("WebHook processed successfully! {0}", testData), body);
        }

        [Fact]
        public async Task TimerTrigger()
        {
            // job is running every second, so give it a few seconds to
            // generate some output
            await Task.Delay(4000);

            string[] lines = File.ReadAllLines("joblog.txt");
            Assert.True(lines.Length > 2);
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture() : base(@"TestScripts\Node")
            {
                File.Delete("joblog.txt");
            }
        }
    }
}

