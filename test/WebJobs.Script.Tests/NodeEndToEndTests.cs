// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
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
        public async Task HttpTest()
        {
            string testData = Guid.NewGuid().ToString();
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/functions/httptrigger")),
                Method = HttpMethod.Get,
                Content = new StringContent(testData)
            };

            await Fixture.Host.CallAsync("HttpTrigger", new { req = request });

            HttpResponseMessage response = (HttpResponseMessage)request.Properties["AzureWebJobs_HttpResponse"];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal(testData, body);
        }

        [Fact]
        public async Task TimerJobTest()
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

                BaseUrl = "http://localhost:46002/";
                Client = new HttpClient();
                Client.BaseAddress = new Uri(BaseUrl);
            }

            public HttpClient Client { get; private set; }

            public string BaseUrl { get; private set; }
        }
    }
}

