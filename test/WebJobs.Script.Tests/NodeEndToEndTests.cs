// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Node;
using Newtonsoft.Json.Linq;
using Xunit;

namespace WebJobs.Script.Tests
{
    public class NodeEndToEndTests : IClassFixture<NodeEndToEndTests.TestFixture>
    {
        private readonly TestFixture _fixture;

        public NodeEndToEndTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task WebHookTest()
        {
            string expectedValue = Guid.NewGuid().ToString();
            string testData = string.Format("{{ \"test\": \"{0}\" }}", expectedValue);
            HttpResponseMessage response = await _fixture.Client.PostAsync("Functions/WebHook", new StringContent(testData));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string json = File.ReadAllText("test.txt");
            string result = (string)JObject.Parse(json)["test"];
            Assert.Equal(expectedValue, result);
        }

        [Fact]
        public async Task ScheduledJobTest()
        {
            // job is running every second, so give it a few seconds to
            // generate some output
            await Task.Delay(4000);

            string[] lines = File.ReadAllLines("joblog.txt");
            Assert.True(lines.Length > 2);
        }

        public class TestFixture : IDisposable
        {
            public TestFixture()
            {
                File.Delete("joblog.txt");

                BaseUrl = "http://localhost:46000/";
                Client = new HttpClient();
                Client.BaseAddress = new Uri(BaseUrl);

                ScriptHostConfiguration config = new ScriptHostConfiguration
                {
                    HostAssembly = Assembly.GetExecutingAssembly(),
                    ApplicationRootPath = Path.Combine(Directory.GetCurrentDirectory(), "node")
                };
                Host = NodeScriptHost.Create(config);
                Host.Start();
            }

            public HttpClient Client { get; private set; }

            public JobHost Host { get; private set; }

            public string BaseUrl { get; private set; }

            public void Dispose()
            {
                Host.Stop();
                Host.Dispose();
            }
        }
    }
}
