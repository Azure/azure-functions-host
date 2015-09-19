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
using Xunit;

namespace WebJobs.Script.Tests
{
    public class CSharpEndToEndTests : IClassFixture<CSharpEndToEndTests.TestFixture>
    {
        private readonly TestFixture _fixture;

        public CSharpEndToEndTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task WebHookTest()
        {
            string testData = Guid.NewGuid().ToString();
            HttpResponseMessage response = await _fixture.Client.PostAsync("Functions/WebHook", new StringContent(testData));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string result = (string)Functions.InvokeData;
            Assert.Equal(testData, result);
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

                BaseUrl = "http://localhost:45000/";
                Client = new HttpClient();
                Client.BaseAddress = new Uri(BaseUrl);

                ScriptConfiguration config = new ScriptConfiguration
                {
                    HostAssembly = Assembly.GetExecutingAssembly(),
                    ApplicationRootPath = Path.Combine(Directory.GetCurrentDirectory(), "csharp")
                };
                Host = CSharpScriptHost.Create(config);
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
