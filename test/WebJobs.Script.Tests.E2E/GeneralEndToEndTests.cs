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

        [Fact]
        [TestTrace]
        public async Task Invocation_Logs_AreReturned()
        {
            using (var client = CreateClient())
            {
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

                string invocationId = await client.GetStringAsync($"api/GetInvocationId?code={_fixture.FunctionDefaultKey}");

                string siteName = NormalizeFunctionName(Settings.SiteName);
                JToken resultToken = null;
                // We retry for a bit as data may take a while to become available
                for (int i = 0; i < 20; i++)
                {
                    var invocationsRequest = new HttpRequestMessage(HttpMethod.Get, $"azurejobs/api/functions/definitions/{siteName}-GetInvocationId/invocations?limit=10");
                    var response = await _fixture.KuduClient.SendAsync(invocationsRequest);

                    var results = await response.Content.ReadAsAsync<JObject>();

                    resultToken = results.SelectToken($"$..entries[?(@.id == '{invocationId}')]");

                    if (resultToken != null)
                    {
                        break;
                    }

                    await Task.Delay(3000);
                }

                _fixture.Assert.True(resultToken != null);
            }
        }

        [Fact]
        [TestTrace]
        public async Task ServiceBus_Node_DoesNotExhaustConnections()
        {
            var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsServiceBus");
            NamespaceManager manager = NamespaceManager.CreateFromConnectionString(connectionString);

            // Start with an empty queue
            await manager.DeleteQueueAsync("node");

            // Pre-create the queue as we can end up with 409s if a bunch of requests
            // try to create the queue at once
            await manager.CreateQueueAsync("node");

            int i = 0, j = 0, lastConnectionCount = 0, lastConnectionLimit = 0;

            using (var client = CreateClient())
            {
                // make this longer as we'll start seeing long timeouts from Service Bus upon failure.
                client.Timeout = TimeSpan.FromMinutes(5);

                // max connections in dynamic is currently 300
                for (i = 0; i < 25; i++)
                {
                    List<Task<HttpResponseMessage>> requestTasks = new List<Task<HttpResponseMessage>>();

                    for (j = 0; j < 25; j++)
                    {
                        requestTasks.Add(client.GetAsync($"api/ServiceBusNode?code={_fixture.FunctionDefaultKey}"));
                    }

                    await Task.WhenAll(requestTasks);

                    foreach (var requestTask in requestTasks)
                    {
                        HttpResponseMessage response = await requestTask;
                        JObject result = await response.Content.ReadAsAsync<JObject>();

                        if (response.IsSuccessStatusCode)
                        {
                            // store these off for error details
                            lastConnectionCount = (int)result["connections"];
                            lastConnectionLimit = (int)result["connectionLimit"];

                            // make sure we have the correct limit
                            Assert.Equal(300, lastConnectionLimit);
                        }

                        Assert.True(response.IsSuccessStatusCode, $"Error: {response.StatusCode}, Last successful response: Connections: {lastConnectionCount}, ConnectionLimit: {lastConnectionLimit}");
                    }
                }
            }

            QueueDescription queueDescription = manager.GetQueue("node");
            Assert.Equal(i * j, queueDescription.MessageCountDetails.ActiveMessageCount);
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

        [Fact]
        [TestTrace]
        public async Task LongRoute()
        {
            var longRoute = "test123412341234123412341234123412341234123412341234123412341234123412341234123421341234123423141234123412341234123412341234123412341234123412341234123412341234123412341234123412341234213423141234123412341234123412341234123412341234123412341234123412341234123412341234123412341234";
            using (var client = CreateClient())
            {
                HttpResponseMessage response = await client.GetAsync(longRoute);

                string content = await response.Content.ReadAsStringAsync();

                // This is to make sure the url is greater than the default asp.net 260 characters.
                _fixture.Assert.True(longRoute.Length > 260);
                _fixture.Assert.Equals("200", response.StatusCode.ToString("D"));
            }
        }

        // Assumes we have a valid function name.
        // Function names are case-insensitive, case-preserving. 
        // Table storage is case-sensitive. So need to normalize case to use as table keys. 
        // Normalize must be one-to-one to avoid collisions. 
        // Escape any non-alphanumeric characters so that we 
        //  a) have a valid rowkey name 
        //  b) don't have characeters that conflict with separators in the row key (like '-')
        public static string NormalizeFunctionName(string functionName)
        {
            var sb = new StringBuilder();
            foreach (var ch in functionName)
            {
                if (ch >= 'a' && ch <= 'z')
                {
                    sb.Append(ch);
                }
                else if (ch >= 'A' && ch <= 'Z')
                {
                    sb.Append((char)(ch - 'A' + 'a'));
                }
                else if (ch >= '0' && ch <= '9')
                {
                    sb.Append(ch);
                }
                else
                {
                    sb.Append(EscapeStorageCharacter(ch));
                }
            }
            return sb.ToString();
        }

        private static string EscapeStorageCharacter(char character)
        {
            var ordinalValue = (ushort)character;
            if (ordinalValue < 0x100)
            {
                return string.Format(CultureInfo.InvariantCulture, ":{0:X2}", ordinalValue);
            }
            else
            {
                return string.Format(CultureInfo.InvariantCulture, "::{0:X4}", ordinalValue);
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
