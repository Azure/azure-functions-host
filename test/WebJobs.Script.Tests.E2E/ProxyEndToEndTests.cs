// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using Newtonsoft.Json.Linq;
using Xunit;

namespace WebJobs.Script.EndToEndTests
{
    [Collection(Constants.FunctionAppCollectionName)]
    public class ProxyEndToEndTests
    {
        private readonly FunctionAppFixture _fixture;

        public ProxyEndToEndTests(FunctionAppFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        [TestTrace]
        public async Task FileExtension()
        {
            using (var client = CreateClient())
            {
                HttpResponseMessage response = await client.GetAsync($"test.txt");

                string content = await response.Content.ReadAsStringAsync();
                _fixture.Assert.Equals("200", response.StatusCode.ToString("D"));
                _fixture.Assert.Equals("test", content);
            }
        }

        [Fact]
        [TestTrace]
        public async Task RootCheck()
        {
            using (var client = CreateClient())
            {
                HttpResponseMessage response = await client.GetAsync("/");

                string content = await response.Content.ReadAsStringAsync();
                _fixture.Assert.Equals("200", response.StatusCode.ToString("D"));
                _fixture.Assert.Equals("Root", content);
            }
        }

        [Fact]
        [TestTrace]
        public async Task LocalFunctionCall()
        {
            using (var client = CreateClient())
            {
                HttpResponseMessage response = await client.GetAsync($"myhttptrigger?code={_fixture.FunctionDefaultKey}");

                string content = await response.Content.ReadAsStringAsync();
                _fixture.Assert.Equals("200", response.StatusCode.ToString("D"));
                _fixture.Assert.Equals("Pong", content);
            }
        }

        public HttpClient CreateClient()
        {
            return new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60),
                BaseAddress = Settings.SiteBaseAddress
            };
        }
    }
}
