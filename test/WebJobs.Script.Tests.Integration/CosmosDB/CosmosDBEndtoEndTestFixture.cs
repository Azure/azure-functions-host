// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Azure.WebJobs.Script.Tests.CosmosDB
{
    public abstract class CosmosDBEndtoEndTestFixture : EndToEndTestFixture
    {
        [SuppressMessage("Microsoft.Security", "CS002:SecretInNextLine", Justification = "Well known account key for emulator. Used for testing.")]
        private static string CosmosDBConnection => "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

        protected CosmosDBEndtoEndTestFixture(string rootPath, string testId, string language) :
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
                        Version = "4.11.0"
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
                    "CosmosDBOut",
                    "CosmosDBInMultiple",
                    "CosmosDBOutMultiple"
                };
            });
        }

        public override void ConfigureScriptHost(IConfigurationBuilder configBuilder)
        {
            base.ConfigureScriptHost(configBuilder);
        }

        public void InitializeCosmosClient()
        {
            if (CosmosClient is null)
            {
                CosmosClient = new CosmosClient(CosmosDBConnection);
            }
        }

        public override async Task InitializeAsync()
        {
            InitializeCosmosClient();
            await SetUpTriggerListener();
            await base.InitializeAsync();
        }

        public override async Task DisposeAsync()
        {
            await base.DisposeAsync();
            await RemoveTriggerDb();
            CosmosClient?.Dispose();
        }

        public async Task<bool> CreateContainers(string dbName)
        {
            bool collectionsCreated = false;

            DatabaseResponse databaseResponse = await CosmosClient.CreateDatabaseIfNotExistsAsync(dbName);
            Database database = databaseResponse.Database;

            ContainerProperties itemCollectionProperties = new ContainerProperties("ItemCollection", "/id");
            ContainerResponse itemCollectionResponse = await database.CreateContainerIfNotExistsAsync(itemCollectionProperties, throughput: 400);

            ContainerProperties leasesCollectionProperties = new ContainerProperties("leases", "/id");
            ContainerResponse leasesCollectionResponse = await database.CreateContainerIfNotExistsAsync(leasesCollectionProperties, throughput: 400);

            if ((itemCollectionResponse.StatusCode == System.Net.HttpStatusCode.Created || itemCollectionResponse.StatusCode == System.Net.HttpStatusCode.OK) &&
                (leasesCollectionResponse.StatusCode == System.Net.HttpStatusCode.Created || leasesCollectionResponse.StatusCode == System.Net.HttpStatusCode.OK))
            {
                collectionsCreated = true;
            }

            return collectionsCreated;
        }

        public async Task DeleteCosmosDbResources(string dbName)
        {
            Database database = CosmosClient.GetDatabase(dbName);

            // Delete the "ItemCollection" container
            Container itemCollectionContainer = database.GetContainer("ItemCollection");
            await itemCollectionContainer.DeleteContainerAsync();

            // Delete the "leases" container
            Container leasesContainer = database.GetContainer("leases");
            await leasesContainer.DeleteContainerAsync();

            await database.DeleteAsync();
        }

        // Regardless of which function is being tested, the trigger listener needs to be set up or the test host fails
        private async Task SetUpTriggerListener()
        {
            var dbName = "TriggerItemDb";
            bool collectionsCreated = await CreateContainers(dbName);
        }

        private async Task RemoveTriggerDb()
        {
            var dbName = "TriggerItemDb";
            await DeleteCosmosDbResources(dbName);
        }
    }
}
