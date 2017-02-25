// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Xunit;

namespace WebJobs.Script.EndToEndTests
{
    [Collection(Constants.FunctionAppCollectionName)]
    public class GeneralEndToEndTests
    {
        private readonly FunctionAppFixture _fixture;

        public GeneralEndToEndTests(FunctionAppFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        [TestTrace]
        public async Task Ping_ReturnsExpectedValue()
        {
            var coldStartTelemetry = new MetricTelemetry();
            coldStartTelemetry.Name = "PingColdStart";

            using (var client = CreateClient())
            {
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

                for (int i = 0; i < 3; i++)
                {
                    // Make sure these tests run against a cold site
                    await _fixture.RestartSite();
                    HttpResponseMessage message = null;
                    using (var operation = _fixture.Telemetry.StartOperation<RequestTelemetry>("ColdStart"))
                    {
                        message = await client.GetAsync($"api/Ping?code={_fixture.FunctionDefaultKey}");
                        operation.Telemetry.ResponseCode = message.StatusCode.ToString("D");
                    }

                    string response = await message.Content.ReadAsStringAsync();
                    _fixture.Assert.Equals("200", message.StatusCode.ToString("D"));
                    _fixture.Assert.Equals("Pong", response);
                }
            }
        }

        [Fact]
        [TestTrace]
        public async Task Version_MatchesExpectedVersion()
        {
            using (var client = CreateClient())
            {
                HttpResponseMessage response = await client.GetAsync($"/admin/host/status?code={_fixture.FunctionAppMasterKey}");

                var status = await response.Content.ReadAsAsync<dynamic>();

                _fixture.Assert.Equals(Settings.RuntimeVersion, status.version.ToString());
            }
        }

        [Fact]
        [TestTrace]
        public async Task AppSettingInformation_ReturnsAppSettingValue()
        {
            using (var client = CreateClient())
            {
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

                string response = await client.GetStringAsync($"api/appsettinginformation?code={_fixture.FunctionDefaultKey}");

                _fixture.Assert.Equals("~1", response);
            }
        }

        [Fact]
        [TestTrace]
        public async Task ParallelRequests_ReturnExpectedResults()
        {
            using (var client = CreateClient())
            {
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

                var responseTasks = new List<Task<HttpResponseMessage>>();

                for (int i = 0; i < 10; i++)
                {
                    var task = client.GetAsync($"api/ping?code={_fixture.FunctionDefaultKey}");
                    responseTasks.Add(task);
                }

                await Task.WhenAll(responseTasks);

                bool requestsSucceeded = responseTasks.TrueForAll(t =>
                 {
                     t.Result.EnsureSuccessStatusCode();
                     return string.Equals("Pong", t.Result.Content.ReadAsStringAsync().Result);
                 });

                _fixture.Assert.True(requestsSucceeded);
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
