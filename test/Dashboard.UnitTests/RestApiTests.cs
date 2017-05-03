// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
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
    [Trait("SecretsRequired", "true")]
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


        // test the /verison endpoint. 
        [Fact]
        public async Task GetVersion()
        {
            var response = await _client.GetJsonAsync<VersionResponse>(_endpoint + "/api/version");

            var assembly = typeof(WebApiConfig).Assembly;
            AssemblyFileVersionAttribute fileVersionAttr = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();

            Assert.Equal(response.Version, fileVersionAttr.Version);
        }

        [Fact]
        public async Task GetDefinitionSkipStats()
        {
            var response = await _client.GetJsonAsync<FunctionStatisticsSegment>(_endpoint + "/api/functions/definitions?limit=100&skipstats=true");

            // Testing against specific data that we added. 
            Assert.Equal(null, response.ContinuationToken);
            var x = response.Entries.ToArray();
            Assert.Equal(1, x.Length);
            Assert.Equal("alpha", x[0].functionName);
            Assert.Equal(0, x[0].successCount); // Skipped 
            Assert.Equal(0, x[0].failedCount);
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
        public async Task GetTimelineInvocations()
        {
            // Lookup functions by name 
            string uri = _endpoint + "/api/functions/invocations/" + FunctionId.Build(Fixture.HostName, "alpha") + 
                "/timeline?limit=11&start=2001-01-01";
            var response = await _client.GetJsonAsync<TimelineResponseEntry[]>(uri);
            
            // This only includes completed / failed functions, not NeverFinished/Running. 
            // Important that DateTimes include the 'Z' suffix, meaning UTC timezone. 
            Assert.Equal(2, response.Length);
            Assert.Equal("2010-03-06T18:10:00Z", response[0].Start);
            Assert.Equal(1, response[0].TotalFail);
            Assert.Equal(0, response[0].TotalPass);
            Assert.Equal(1, response[0].TotalRun);

            Assert.Equal("2010-03-06T18:11:00Z", response[1].Start);
            Assert.Equal(0, response[1].TotalFail);
            Assert.Equal(1, response[1].TotalPass);
            Assert.Equal(1, response[1].TotalRun);
        }

        public async Task GetTimelineEmptyInvocations()
        {
            // Look in timeline range where there's no functions invocations. 
            // This verifies the time range is getting parsed by the webapi and passed through. 
            string uri = _endpoint + "/api/functions/invocations/" + FunctionId.Build(Fixture.HostName, "alpha") + 
                "/timeline?limit=11&start=2005-01-01";
            var response = await _client.GetJsonAsync<TimelineResponseEntry[]>(uri);

            Assert.Equal(0, response.Length);
        }


        [Fact]
        public async Task GetInvocations()
        {
            // Lookup functions by name 
            string uri = _endpoint + "/api/functions/definitions/" + FunctionId.Build(Fixture.HostName, "alpha") + "/invocations?limit=11";
            var response = await _client.GetJsonAsync<DashboardSegment<InvocationLogViewModel>>(uri);

            Assert.Equal(_fixture.ExpectedItems.Count, response.entries.Length);

            for (int i = 0; i < response.entries.Length; i++)
            {
                var expectedItem = _fixture.ExpectedItems[i];
                var actualItem = response.entries[i];

                AssertEqual(expectedItem, actualItem);

                Assert.Equal("alpha", actualItem.functionDisplayTitle);
            }
        }

        [Fact]
        public async Task GetSpecificInvocation()
        {
            foreach (var expectedItem in _fixture.ExpectedItems)
            {
                var url = _endpoint + "/api/functions/invocations/" + expectedItem.id;
                var response = await _client.GetJsonAsync<FunctionInstanceDetailsViewModel>(url);

                var actualItem = response.Invocation;
                AssertEqual(expectedItem, actualItem);

                Assert.Equal("alpha ()", response.Invocation.functionDisplayTitle);

                Assert.Equal(actualItem.id, response.TriggerReason.childGuid);
            }
        }

        private static void AssertEqual(InvocationLogViewModel expectedItem, InvocationLogViewModel actualItem)
        {
            Assert.Equal(expectedItem.id, actualItem.id);
            Assert.Equal(expectedItem.whenUtc, actualItem.whenUtc);
            Assert.Equal(expectedItem.status, actualItem.status);
            if (actualItem.status != "Running")
            {
                Assert.Equal(expectedItem.duration, actualItem.duration);
            }
            else
            {
                // Compares to current time. 
                var minValue = DateTime.UtcNow.AddDays(-1) - DateTime.Parse(actualItem.whenUtc);
                var totalMs = minValue.TotalMilliseconds;

                Assert.True(actualItem.duration.Value > totalMs);
            }
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

            public List<FunctionInstanceLogItem> Data {get; private set; }
            public List<InvocationLogViewModel> ExpectedItems { get; private set; }

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
                await WriteTestLoggingDataAsync(_provider);

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
            private async Task WriteTestLoggingDataAsync(ILogTableProvider provider)
            {
                ILogWriter writer = LogFactory.NewWriter(HostName, "c1", provider);

                string Func1 = "alpha";
                var time = new DateTime(2010, 3, 6, 18, 11, 20, DateTimeKind.Utc);

                List<FunctionInstanceLogItem> list = new List<FunctionInstanceLogItem>();
                List<InvocationLogViewModel> expected = new List<InvocationLogViewModel>();
                this.ExpectedItems = expected;
                this.Data = list;

                // List in reverse chronology. 
                // Completed Success
                {
                    var item = new FunctionInstanceLogItem
                    {
                        FunctionInstanceId = Guid.NewGuid(),
                        FunctionName = Func1,
                        StartTime = time,
                        EndTime = time.AddMinutes(2),  // Completed 
                        LogOutput = "one",
                    };
                    list.Add(item);
                    expected.Add(new InvocationLogViewModel
                    {
                        id = item.FunctionInstanceId.ToString(),
                        status = "CompletedSuccess",
                        whenUtc = "2010-03-06T18:13:20Z", // since it's completed, specifies end-time 
                        duration = 120000.0
                    });
                }

                // Completed Error 
                {
                    time = time.AddMinutes(-1);
                    var item = new FunctionInstanceLogItem
                    {
                        FunctionInstanceId = Guid.NewGuid(),
                        FunctionName = Func1,
                        StartTime = time,
                        EndTime = time.AddMinutes(2),
                        ErrorDetails = "some failure", // signifies failure 
                        LogOutput = "two",
                    };
                    list.Add(item);
                    expected.Add(new InvocationLogViewModel
                    {
                        id = item.FunctionInstanceId.ToString(),
                        status = "CompletedFailed",
                        whenUtc = "2010-03-06T18:12:20Z", // end-time. 
                        duration = 120000.0
                    });
                }

                // Still running 
                {
                    time = time.AddMinutes(-1);
                    var item = new FunctionInstanceLogItem
                    {
                        FunctionInstanceId = Guid.NewGuid(),
                        FunctionName = Func1,
                        StartTime = time,  // Recent heartbeat 
                        LogOutput = "two",
                    };
                    list.Add(item);
                    expected.Add(new InvocationLogViewModel
                    {
                        id = item.FunctionInstanceId.ToString(),
                        status = "Running",
                        whenUtc = "2010-03-06T18:09:20Z", // specifies start-time
                    });
                }

                // Never Finished
                {
                    time = time.AddMinutes(-1);
                    var item = new TestFunctionInstanceLogItem
                    {
                        FunctionInstanceId = Guid.NewGuid(),
                        FunctionName = Func1,
                        StartTime = time,  // Never Finished
                        LogOutput = "two",
                        OnRefresh = (me) => { me.FunctionInstanceHeartbeatExpiry = time; },// stale heartbeat
                    };
                    list.Add(item);
                    expected.Add(new InvocationLogViewModel
                    {
                        id = item.FunctionInstanceId.ToString(),
                        status = "NeverFinished",
                        whenUtc = "2010-03-06T18:08:20Z", // starttime
                        duration = null
                    });
                }

                // No heartbeat (legacy example)
                {
                    time = time.AddMinutes(-1);
                    var item = new TestFunctionInstanceLogItem
                    {
                        FunctionInstanceId = Guid.NewGuid(),
                        FunctionName = Func1,
                        StartTime = time,  // Never Finished 
                        LogOutput = "two",
                        OnRefresh = (me) => { } // No heart beat 
                    };
                    list.Add(item);
                    expected.Add(new InvocationLogViewModel
                    {
                        id = item.FunctionInstanceId.ToString(),
                        status = "Running",
                        whenUtc = "2010-03-06T18:07:20Z", // starttime
                    });
                } 

                foreach (var item in list)
                {
                    await writer.AddAsync(item);
                }

                await writer.FlushAsync();               
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

            public class TestFunctionInstanceLogItem : FunctionInstanceLogItem
            {
                public Action<TestFunctionInstanceLogItem> OnRefresh;
                public override void Refresh(TimeSpan pollingFrequency)
                {
                    OnRefresh(this);
                }
            }

        }


    }
}