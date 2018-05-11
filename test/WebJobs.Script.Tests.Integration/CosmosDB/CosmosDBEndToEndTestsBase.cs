// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Queue;
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
            // CosmosDB tests need the following environment vars:
            // "AzureWebJobsCosmosDBConnectionString" -- the connection string to the account

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

            // now wait for function to be invoked
            string result = await TestHelpers.WaitForBlobAndGetStringAsync(resultBlob,
                () => string.Join(Environment.NewLine, Fixture.Host.GetLogMessages()));

            if (collectionsCreated)
            {
                // cleanup collections
                await Fixture.DeleteDocumentCollections();
            }

            Assert.False(string.IsNullOrEmpty(result));
        }

        protected async Task CosmosDBTest()
        {
            // DocumentDB tests need the following environment vars:
            // "AzureWebJobsCosmosDBConnectionString" -- the connection string to the account
            string id = Guid.NewGuid().ToString();

            await Fixture.Host.BeginFunctionAsync("CosmosDBOut", id);

            Document doc = await WaitForDocumentAsync(id);

            Assert.Equal(doc.Id, id);

            // Now add that Id to a Queue, in an object to test binding
            var queue = await Fixture.GetNewQueue("documentdb-input");
            string messageContent = string.Format("{{ \"documentId\": \"{0}\" }}", id);
            await queue.AddMessageAsync(new CloudQueueMessage(messageContent));

            // And wait for the text to be updated
            Document updatedDoc = await WaitForDocumentAsync(id, "This was updated!");

            Assert.Equal(updatedDoc.Id, doc.Id);
            Assert.NotEqual(doc.ETag, updatedDoc.ETag);
        }

    }

    public abstract class CosmosDBTestFixture : EndToEndTestFixture
    {
        protected CosmosDBTestFixture(string rootPath, string testId) :
            base(rootPath, testId, "Microsoft.Azure.WebJobs.Extensions.CosmosDB", "3.0.0-beta7-10602")
        {
        }

        public DocumentClient DocumentClient { get; private set; }

        protected override IEnumerable<string> GetActiveFunctions() => new[] { "CosmosDBTrigger", "CosmosDBIn", "CosmosDBOut" };

        public async Task InitializeDocumentClient()
        {
            if (DocumentClient == null)
            {
                var builder = new System.Data.Common.DbConnectionStringBuilder();
                builder.ConnectionString = AmbientConnectionStringProvider.Instance.GetConnectionString("AzureWebJobsCosmosDBConnectionString");
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
