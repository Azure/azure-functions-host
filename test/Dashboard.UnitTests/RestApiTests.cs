// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using Dashboard.UnitTests.RestProtocol;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Xunit;

namespace Dashboard.UnitTests
{
    // Test calling the REST API surface. 
    // This surface area is important because the APIs are public and called by the Portal.
    // This explicitly goes through the HttpClient and tests things like serialization, rest interfaces, etc. 
    public class RestApiTests : IClassFixture<RestApiTests.Fixture>
    {
        private readonly Fixture _fixture;
        private readonly HttpClient _client;
        private readonly string _endpoint;

        public RestApiTests(Fixture fixture)
        {
            _fixture = fixture;
            _client = fixture.Client;
            _endpoint = fixture.Endpoint;
        }

        [Fact]
        public async Task GetDefinition()
        {
            var response = await _client.GetJsonAsync<FunctionStatisticsSegment>(_endpoint + "/api/functions/definitions?limit=100");

            // Testing against specific data that we added. 
            Assert.Equal(null, response.ContinuationToken);
            var x = response.Entries.ToArray();
            Assert.Equal(1, x.Length);
            Assert.Equal("alpha", x[0].functionName);
        }

        // Pass ?host=  parameter when getting definitions to filter to a specific host. 
        [Fact]
        public async Task GetDefinitionSingleHost()
        {
            var response = await _client.GetJsonAsync<FunctionStatisticsSegment>(_endpoint + "/api/functions/definitions?limit=100&host=missing");

            // Testing against specific data that we added. 
            Assert.Equal(null, response.ContinuationToken);
            var x = response.Entries.ToArray();
            Assert.Equal(0, x.Length);            
        }

        [Fact]
        public async Task GetInvocations()
        {
            // Lookup functions by name 
            string uri = _endpoint + "/api/functions/definitions/" + FunctionId.Build(Fixture.HostName, "alpha") + "/invocations?limit=11";
            var response = await _client.GetJsonAsync<DashboardSegment<InvocationLogViewModel>>(uri);

            var item = _fixture.Data[0];

            var x = response.entries.ToArray();
            Assert.Equal(item.FunctionInstanceId.ToString(), x[0].id);
            Assert.Equal("2010-03-06T18:13:20Z", x[0].whenUtc);
            Assert.Equal(120000.0, x[0].duration);
            Assert.Equal("CompletedSuccess", x[0].status);
            Assert.Equal("alpha", x[0].functionDisplayTitle);
        }

        [Fact]
        public async Task GetSpecificInvocation()
        {
            var item = _fixture.Data[0];

            // Lookup specific invocation 

            var url = _endpoint + "/api/functions/invocations/" + item.FunctionInstanceId.ToString();
            var response2 = await _client.GetJsonAsync<FunctionInstanceDetailsViewModel>(url);

            Assert.Equal(item.FunctionName, response2.Invocation.functionName);
            Assert.Equal(item.FunctionInstanceId.ToString(), response2.Invocation.id);
            Assert.Equal("CompletedSuccess", response2.Invocation.status);
            Assert.Equal(120000.0, response2.Invocation.duration);
            Assert.Equal("alpha ()", response2.Invocation.functionDisplayTitle);

            Assert.Equal(item.FunctionInstanceId.ToString(), response2.TriggerReason.childGuid);
        }

        [Fact]
        public async Task GetLogOutput()
        {
            var item = _fixture.Data[0];

            var responseLog = await _client.GetAsync(_endpoint + "/api/log/output/" + item.FunctionInstanceId.ToString());
            Assert.Equal(HttpStatusCode.OK, responseLog.StatusCode);
            string str = await responseLog.Content.ReadAsStringAsync();
            Assert.Equal(item.LogOutput, str);
        }

        // Shared context across tests.
        // This creates HttpServer & Client to access the rest APIs. 
        // It also creates some baseline logging data to test against. 
        public class Fixture : IDisposable
        {
            private const string FunctionLogTableAppSettingName = "AzureWebJobsLogTableName";

            public static string HostName = "Host";

            private ILogTableProvider _provider;

            public HttpClient Client { get; private set; }
            public string Endpoint { get; private set; }
            public FunctionInstanceLogItem[] Data
            {
                get; private set;
            }

            public Fixture()
            {
                Init().Wait();
            }

            public async Task Init()
            {
                var tableClient = GetNewLoggingTableClient();                
                var tablePrefix = "logtesZZ" + Guid.NewGuid().ToString("n");
                ConfigurationManager.AppSettings[FunctionLogTableAppSettingName] = tablePrefix; // tell dashboard to use it
                _provider = LogFactory.NewLogTableProvider(tableClient, tablePrefix);
                this.Data = await WriteTestLoggingDataAsync(_provider);

                var config = new HttpConfiguration();

                var container = MvcApplication.BuildContainer(config);

                WebApiConfig.Register(config);

                var server = new HttpServer(config);
                var client = new HttpClient(server);

                this.Client = client;
                this.Endpoint = "http://localhost:8080"; // ignored
            }

            public void Dispose()
            {
                DisposeAsync().Wait();
            }
            private async Task DisposeAsync()
            {
                var tables = await _provider.ListTablesAsync();
                Task[] tasks = Array.ConvertAll(tables, table => table.DeleteIfExistsAsync());
                await Task.WhenAll(tasks);                
            }

            // Write logs. Return what we wrote. 
            // This is baseline data. REader will verify against it exactly. This helps in aggressively catching subtle breaking changes. 
            private async Task<FunctionInstanceLogItem[]> WriteTestLoggingDataAsync(ILogTableProvider provider)
            {
                ILogWriter writer = LogFactory.NewWriter(HostName, "c1", provider);

                string Func1 = "alpha";
                var time = new DateTime(2010, 3, 6, 18, 11, 20, DateTimeKind.Utc);

                List<FunctionInstanceLogItem> list = new List<FunctionInstanceLogItem>();
                list.Add(new FunctionInstanceLogItem
                {
                    FunctionInstanceId = Guid.NewGuid(),
                    FunctionName = Func1,
                    StartTime = time,
                    EndTime = time.AddMinutes(2),
                    LogOutput = "one",
                    Status = Microsoft.Azure.WebJobs.Logging.FunctionInstanceStatus.CompletedSuccess
                });

                foreach (var item in list)
                {
                    await writer.AddAsync(item);
                }

                await writer.FlushAsync();
                return list.ToArray();
            }

            CloudTableClient GetNewLoggingTableClient()
            {
                string storageString = "AzureWebJobsDashboard";
                var acs = Environment.GetEnvironmentVariable(storageString);
                if (acs == null)
                {
                    Assert.True(false, "Environment var " + storageString + " is not set. Should be set to an azure storage account connection string to use for testing.");
                }
                
                CloudStorageAccount account = CloudStorageAccount.Parse(acs);
                var client = account.CreateCloudTableClient();
                return client;
            }
        }


    }
}