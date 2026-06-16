// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Azure.Cosmos;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.CosmosDB
{
    public abstract class CosmosDBEndToEndTestsBase<TTestFixture> :
        EndToEndTestsBase<TTestFixture> where TTestFixture : CosmosDBEndToEndTestFixture, new()
    {
        private readonly CosmosDBEndToEndTestFixture _fixture;

        public CosmosDBEndToEndTestsBase(TTestFixture fixture) : base(fixture)
        {
            _fixture = fixture;
        }

        protected async Task CosmosDBTriggerToBlobTest()
        {
            // Waiting for the Processor to acquire leases
            await Task.Delay(10000);
            _fixture.AssertNoScriptHostErrors();

            var dbName = "TriggerItemDb";

            var resultBlob = _fixture.TestOutputContainer.GetBlobClient("cosmosdbtriggere2e-completed");
            await resultBlob.DeleteIfExistsAsync();

            string id = Guid.NewGuid().ToString();
            var documentToTest = new { id };

            await _fixture.CosmosClient.GetContainer(dbName, "ItemCollection")
                .CreateItemAsync(documentToTest, new PartitionKey(id));

            // now wait for function to be invoked
            string result = await TestHelpers.WaitForBlobAndGetStringAsync(
                resultBlob,
                content => !string.IsNullOrEmpty(content),
                userMessageCallback: () => string.Join(Environment.NewLine, _fixture.Host.GetScriptHostLogMessages()));

            Assert.False(string.IsNullOrEmpty(result));

            await resultBlob.DeleteIfExistsAsync();
        }

        protected async Task CosmosDBTest()
        {
            _fixture.AssertNoScriptHostErrors();

            var dbName = "InOutItemDb";
            await _fixture.CreateContainers(dbName);

            string id = Guid.NewGuid().ToString();
            await _fixture.Host.BeginFunctionAsync("CosmosDBOut", id);

            dynamic doc = await WaitForItemAsync(id, dbName);
            Assert.Equal((string)doc.id, id);

            var queue = await _fixture.GetNewQueue("cosmosdb-input");
            string messageContent = string.Format("{{ \"id\": \"{0}\" }}", id);
            await queue.SendMessageAsync(messageContent);

            // And wait for the text to be updated
            dynamic updatedDoc = await WaitForItemAsync(id, dbName, "This was updated!");

            Assert.Equal(updatedDoc.id, doc.id);
            Assert.NotEqual(doc._etag, updatedDoc._etag);

            await _fixture.DeleteCosmosDbResources(dbName);
        }

        protected async Task CosmosDBMultipleItemsTest()
        {
            _fixture.AssertNoScriptHostErrors();

            var dbName = "MultipleInOutItemDb";
            await _fixture.CreateContainers(dbName);

            string id = Guid.NewGuid().ToString();
            await _fixture.Host.BeginFunctionAsync("CosmosDBOutMultiple", id);
            var testId = id + "-0";
            await WaitForItemAsync(testId, dbName);

            var queue = await _fixture.GetNewQueue("cosmosdb-input-multiple");
            string messageContent = string.Format("{{ \"id\": \"{0}\" }}", id);
            await queue.SendMessageAsync(messageContent);

            // And wait for the text to be updated
            await WaitForItemAsync(id, dbName, "Hello from Node with multiple input bindings!");

            await _fixture.DeleteCosmosDbResources(dbName);
        }

        protected async Task<dynamic> WaitForItemAsync(string itemId, string itemDb, string textToMatch = null)
        {
            var container = _fixture.CosmosClient.GetContainer(itemDb, "ItemCollection");

            dynamic document = null;

            await TestHelpers.Await(async () =>
            {
                bool result = false;

                try
                {
                    var response = await container.ReadItemAsync<dynamic>(itemId, new PartitionKey(itemId));
                    document = response.Resource;

                    result = textToMatch is null || document.text == textToMatch;
                }
                catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Document not found, continue waiting
                    return false;
                }

                return result;
            },
            userMessageCallback: () =>
            {
                var logs = string.Join(Environment.NewLine, _fixture.Host.GetScriptHostLogMessages());
                return logs;
            });

            return document;
        }
    }
}
