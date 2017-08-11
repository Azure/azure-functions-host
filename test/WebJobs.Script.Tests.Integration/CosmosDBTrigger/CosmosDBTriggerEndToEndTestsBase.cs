// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.WebJobs.Host;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.CosmosDBTrigger
{
    public abstract class CosmosDBTriggerEndToEndTestsBase<TTestFixture> :
        EndToEndTestsBase<TTestFixture> where TTestFixture : CosmosDBTriggerTestFixture, new()
    {
        public CosmosDBTriggerEndToEndTestsBase(TTestFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task CosmosDBTriggerToBlobTest()
        {
            // DocumentDB tests need the following environment vars:
            // "AzureWebJobsDocumentDBConnectionString" -- the connection string to the account

            // Waiting for the Processor to acquire leases
            await Task.Delay(10000);

            await Fixture.InitializeDocumentClient();
            bool collectionsCreated = await Fixture.CreateDocumentCollections();
            var resultBlob = Fixture.TestOutputContainer.GetBlockBlobReference("cosmosdbtriggere2e-completed");
            await resultBlob.DeleteIfExistsAsync();

            string id = Guid.NewGuid().ToString();

            Document documentToTest = new Document()
            {
                Id = id
            };

            await Fixture.DocumentClient.CreateDocumentAsync(UriFactory.CreateDocumentCollectionUri("ItemDb", "ItemCollection"), documentToTest);

            // wait for logs to flush
            await Task.Delay(FileTraceWriter.LogFlushIntervalMs);

            // now wait for function to be invoked
            string result = await TestHelpers.WaitForBlobAndGetStringAsync(resultBlob);

            if (collectionsCreated)
            {
                // cleanup collections
                await Fixture.DeleteDocumentCollections();
            }

            Assert.Equal(TestHelpers.RemoveByteOrderMarkAndWhitespace(id), TestHelpers.RemoveByteOrderMarkAndWhitespace(result));
        }
    }

    public abstract class CosmosDBTriggerTestFixture : EndToEndTestFixture
    {
        protected CosmosDBTriggerTestFixture(string rootPath, string testId) : base(rootPath, testId)
        {
        }

        public DocumentClient DocumentClient { get; private set; }

        protected override void InitializeConfig(ScriptHostConfiguration config)
        {
            base.InitializeConfig(config);

            config.OnConfigurationApplied = c =>
                {
                    // restrict these tests to only use this function
                    c.Functions = new[] { "CosmosDBTrigger" };
                };
        }

        public async Task InitializeDocumentClient()
        {
            if (DocumentClient == null)
            {
                var builder = new System.Data.Common.DbConnectionStringBuilder();
                builder.ConnectionString = AmbientConnectionStringProvider.Instance.GetConnectionString("AzureWebJobsDocumentDBConnectionString");
                var serviceUri = new Uri(builder["AccountEndpoint"].ToString());

                DocumentClient = new DocumentClient(serviceUri, builder["AccountKey"].ToString());
                await DocumentClient.OpenAsync();
            }
        }

        public async Task<bool> CreateDocumentCollections()
        {
            bool willCreateCollection = false;
            Database db = new Database() { Id = "ItemDb" };
            await DocumentClient.CreateDatabaseIfNotExistsAsync(db);
            Uri dbUri = UriFactory.CreateDatabaseUri(db.Id);

            DocumentCollection collection = new DocumentCollection() { Id = "ItemCollection" };
            willCreateCollection = !DocumentClient.CreateDocumentCollectionQuery(dbUri).Where(x => x.Id == collection.Id).ToList().Any();
            await DocumentClient.CreateDocumentCollectionIfNotExistsAsync(dbUri, collection,
                new RequestOptions()
                {
                    OfferThroughput = 400
                });

            Documents.DocumentCollection leasesCollection = new Documents.DocumentCollection() { Id = "leases" };
            await DocumentClient.CreateDocumentCollectionIfNotExistsAsync(dbUri, leasesCollection,
                new RequestOptions()
                {
                    OfferThroughput = 400
                });

            return willCreateCollection;
        }

        public async Task DeleteDocumentCollections()
        {
            Uri collectionsUri = UriFactory.CreateDocumentCollectionUri("ItemDb", "ItemCollection");
            Uri leasesCollectionsUri = UriFactory.CreateDocumentCollectionUri("ItemDb", "leases");
            await DocumentClient.DeleteDocumentCollectionAsync(collectionsUri);
            await DocumentClient.DeleteDocumentCollectionAsync(leasesCollectionsUri);
        }

        public override void Dispose()
        {
            base.Dispose();
            DocumentClient?.Dispose();
        }
    }
}
