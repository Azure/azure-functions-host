// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.CosmosDB
{
    public abstract class CosmosDBEndToEndTestsBase<TTestFixture> :
        EndToEndTestsBase<TTestFixture> where TTestFixture : CosmosDBTestFixture, new()
    {
        public CosmosDBEndToEndTestsBase(TTestFixture fixture) : base(fixture)
        {
        }

        protected async Task CosmosDBTriggerToBlobTest()
        {
            // CosmosDB tests need the following connection string:
            // "ConnectionStrings:CosmosDB" -- the connection string to the account

            // Waiting for the Processor to acquire leases
            await Task.Delay(10000);

            Fixture.InitializeCosmosClient();

            bool collectionsCreated = await Fixture.CreateDocumentCollections();
            var resultBlob = Fixture.TestOutputContainer.GetBlobClient("cosmosdbtriggere2e-completed");
            await resultBlob.DeleteIfExistsAsync();

            string id = Guid.NewGuid().ToString();
            var container = Fixture.CosmosClient.GetContainer("ItemDb", "ItemCollection");
            await container.CreateItemAsync(new { id });

            // now wait for function to be invoked
            string result = await TestHelpers.WaitForBlobAndGetStringAsync(resultBlob,
                () => string.Join(Environment.NewLine, Fixture.Host.GetScriptHostLogMessages()));

            if (collectionsCreated)
            {
                // cleanup collections
                await Fixture.DeleteDocumentCollections();
            }

            Assert.False(string.IsNullOrEmpty(result));
        }

        protected async Task CosmosDBTest()
        {
            // DocumentDB tests need the following connection string:
            // "ConnectionStrings:CosmosDB" -- the connection string to the account
            string id = Guid.NewGuid().ToString();

            await Fixture.Host.BeginFunctionAsync("CosmosDBOut", id);
           
            ItemResponse<JObject> itemResponse = await WaitForDocumentAsync(id);

            Assert.Equal(id, itemResponse.Resource["id"]?.ToString());

            // Now add that Id to a Queue, in an object to test binding
            var queue = await Fixture.GetNewQueue("documentdb-input");
            string messageContent = string.Format("{{ \"documentId\": \"{0}\" }}", id);
            await queue.SendMessageAsync(messageContent);

            // And wait for the text to be updated
            ItemResponse<JObject> updatedItemResponse = await WaitForDocumentAsync(id, "This was updated!");

            Assert.Equal(id, updatedItemResponse.Resource["id"]?.ToString());
            Assert.NotEqual(itemResponse.ETag, updatedItemResponse.ETag);
        }
    }

    public abstract class CosmosDBTestFixture : EndToEndTestFixture
    {
        protected CosmosDBTestFixture(string rootPath, string testId, string language) :
            base(rootPath, testId, language)
        {
        }
        
        public CosmosClient CosmosClient { get; private set; }

        protected override ExtensionPackageReference[] GetExtensionsToInstall()
        {
            return new ExtensionPackageReference[]
            {
                    new ExtensionPackageReference
                    {
                        Id = "Microsoft.Azure.WebJobs.Extensions.CosmosDB",
                        Version = "3.0.10"
                    }
            };
        }

        public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
        {
            webJobsBuilder.Services.Configure<ScriptJobHostOptions>(o =>
            {
                o.Functions = new[]
                {
                    "CosmosDBTrigger",
                    "CosmosDBIn",
                    "CosmosDBOut"
                };
            });
        }

        public void InitializeCosmosClient()
        {
            if (CosmosClient == null)
            {
                var connectionString = TestHelpers.GetTestConfiguration().GetConnectionString("CosmosDB");
                CosmosClient = new CosmosClient(connectionString);
            }
        }

        public async Task<bool> CreateDocumentCollections()
        {
            bool willCreateCollection = false;
            Database database = await CosmosClient.CreateDatabaseIfNotExistsAsync("ItemDb");

            ContainerProperties itemCollectionProperties = new ContainerProperties("ItemCollection", "/_partitionKey");
            ContainerResponse itemCollectionResponse = await database.CreateContainerIfNotExistsAsync(
                itemCollectionProperties,
                throughput: 400);
            willCreateCollection = itemCollectionResponse.StatusCode == System.Net.HttpStatusCode.Created;

            ContainerProperties leasesCollectionProperties = new ContainerProperties("leases", "/_partitionKey");
            await database.CreateContainerIfNotExistsAsync(
                leasesCollectionProperties,
                throughput: 400);

            return willCreateCollection;
        }

        public async Task DeleteDocumentCollections()
        {
            Database database = CosmosClient.GetDatabase("ItemDb");
            await database.GetContainer("ItemCollection").DeleteContainerAsync();
            await database.GetContainer("leases").DeleteContainerAsync();
        }

        public override async Task DisposeAsync()
        {
            await base.DisposeAsync();
            CosmosClient?.Dispose();
        }
    }
}
