// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.WebJobs.Script.Tests;
using Microsoft.WindowsAzure.Storage.Table;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.EndToEnd
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, nameof(SamplesEndToEndTests))]
    public class SamplesEndToEndTestsNoLanguage : IClassFixture<SamplesEndToEndTestsNoLanguage.TestFixture>
    {
        private readonly ScriptSettingsManager _settingsManager;
        private TestFixture _fixture;

        public SamplesEndToEndTestsNoLanguage(TestFixture fixture)
        {
            _fixture = fixture;
            _settingsManager = ScriptSettingsManager.Instance;
        }

        public object AuthorizationLevelAttribute { get; private set; }

        [Fact]
        public async Task HttpTrigger_Get_Succeeds()
        {
            await InvokeHttpTriggerInvalidLanguage("HttpTrigger");
        }

        [Fact]
        public async Task HttpTriggerCSharp_Get_Fails()
        {
            await InvokeHttpTriggerInvalidLanguage("HttpTrigger-CSharp");
        }

        [Fact]
        public async Task HttpTriggerJava_Get_Fails()
        {
            await InvokeHttpTriggerInvalidLanguage("HttpTrigger-Java");
        }

        private async Task InvokeHttpTriggerInvalidLanguage(string functionName)
        {
            string functionKey = await _fixture.Host.GetFunctionSecretAsync($"{functionName}");
            string uri = $"api/{functionName}?code={functionKey}&name=Mathew";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        public class TestFixture : EndToEndTestFixture
        {
            static TestFixture()
            {
                Environment.SetEnvironmentVariable("AzureWebJobs.HttpTrigger-Disabled.Disabled", "1");
            }

            public TestFixture()
                : base(Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\..\sample"), "samples", functionsWorkerRuntime: null)
            {
            }

            public override void ConfigureJobHost(IWebJobsBuilder webJobsBuilder)
            {
                base.ConfigureJobHost(webJobsBuilder);

                webJobsBuilder.Services.Configure<ScriptJobHostOptions>(o =>
                {
                    o.Functions = new[]
                    {
                        "HttpTrigger",
                        "HttpTrigger-Java",
                        "HttpTrigger-CSharp",
                    };
                });
            }

            protected override async Task CreateTestStorageEntities()
            {
                // Don't call base.
                var table = TableClient.GetTableReference("samples");
                await table.CreateIfNotExistsAsync();

                var batch = new TableBatchOperation();
                batch.InsertOrReplace(new TestEntity { PartitionKey = "samples-python", RowKey = "1", Title = "Test Entity 1", Status = 0 });
                batch.InsertOrReplace(new TestEntity { PartitionKey = "samples-python", RowKey = "2", Title = "Test Entity 2", Status = 0 });
                batch.InsertOrReplace(new TestEntity { PartitionKey = "samples-python", RowKey = "3", Title = "Test Entity 3", Status = 1 });
                batch.InsertOrReplace(new TestEntity { PartitionKey = "samples-python", RowKey = "4", Title = "Test Entity 4", Status = 0 });
                batch.InsertOrReplace(new TestEntity { PartitionKey = "samples-python", RowKey = "5", Title = "Test Entity 5", Status = 0 });
                batch.InsertOrReplace(new TestEntity { PartitionKey = "samples-python", RowKey = "6", Title = "Test Entity 6", Status = 0 });
                batch.InsertOrReplace(new TestEntity { PartitionKey = "samples-python", RowKey = "7", Title = "Test Entity 7", Status = 0 });
                await table.ExecuteBatchAsync(batch);
            }

            private class TestEntity : TableEntity
            {
                public string Title { get; set; }

                public int Status { get; set; }
            }
        }
    }
}