// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Storage.Queue;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.CosmosDB
{
    public abstract class CosmosDBEndToEndTestsBase<TTestFixture> :
        EndToEndTestsBase<TTestFixture> where TTestFixture : CosmosDBEndtoEndTestFixture
    {
        public CosmosDBEndToEndTestsBase(TTestFixture fixture) : base(fixture)
        {
        }

        protected async Task CosmosDBTriggerToBlobTest()
        {
            // Waiting for the Processor to acquire leases
            await Task.Delay(10000);

            bool collectionsCreated = await Fixture.CreateContainers();
            var resultBlob = Fixture.TestOutputContainer.GetBlockBlobReference("cosmosdbtriggere2e-completed");
            await resultBlob.DeleteIfExistsAsync();

            string id = Guid.NewGuid().ToString();
            string partitionKeyValue = Guid.NewGuid().ToString();

            var documentToTest = new { id, partitionKey = partitionKeyValue};

            await Fixture.CosmosClient.GetContainer("ItemDb", "ItemCollection")
                .CreateItemAsync(documentToTest, new PartitionKey(partitionKeyValue));

            // now wait for function to be invoked
            string result = await TestHelpers.WaitForBlobAndGetStringAsync(resultBlob,
                () => string.Join(Environment.NewLine, Fixture.Host.GetScriptHostLogMessages()));

            if (collectionsCreated)
            {
                // cleanup collections
                await Fixture.DeleteContainers();
            }

            Assert.False(string.IsNullOrEmpty(result));
        }

        protected async Task CosmosDBTest()
        {
            string id = Guid.NewGuid().ToString();

            await Fixture.Host.BeginFunctionAsync("CosmosDBOut", id);

            dynamic doc = await WaitForItemAsync(id);

            Assert.Equal(doc.id, id);

            // Now add that Id to a Queue, in an object to test binding
            var queue = await Fixture.GetNewQueue("documentdb-input");
            string messageContent = string.Format("{{ \"documentId\": \"{0}\" }}", id);
            await queue.AddMessageAsync(new CloudQueueMessage(messageContent));

            // And wait for the text to be updated
            dynamic updatedDoc = await WaitForItemAsync(id, "This was updated!");

            Assert.Equal(updatedDoc.id, doc.id);
            Assert.NotEqual(doc._etag, updatedDoc._etag);
        }

        protected async Task<dynamic> WaitForItemAsync(string itemId, string textToMatch = null)
        {
            var container = Fixture.CosmosClient.GetContainer("ItemDb", "ItemCollection");

            dynamic document = null;

            await TestHelpers.Await(async () =>
            {
                bool result = false;

                try
                {
                    var response = await container.ReadItemAsync<dynamic>(itemId, new PartitionKey(itemId));
                    document = response.Resource;

                    if (textToMatch != null)
                    {
                        result = document.text == textToMatch;
                    }
                    else
                    {
                        result = true;
                    }
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
                var logs = string.Join(Environment.NewLine, Fixture.Host.GetScriptHostLogMessages());
                return logs;
            });

            return document;
        }
    }
}
