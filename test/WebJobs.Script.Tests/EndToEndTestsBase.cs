// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.EasyTables;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.MobileServices;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json.Linq;
using Xunit;

namespace WebJobs.Script.Tests
{
    [Trait("Category", "E2E")]
    public abstract class EndToEndTestsBase<TTestFixture> :
        IClassFixture<TTestFixture> where TTestFixture : EndToEndTestFixture, new()
    {
        public EndToEndTestsBase(TTestFixture fixture)
        {
            Fixture = fixture;
        }

        protected TTestFixture Fixture { get; private set; }

        [Fact]
        public async Task QueueTriggerToBlobTest()
        {
            string id = Guid.NewGuid().ToString();
            string messageContent = string.Format("{{ \"id\": \"{0}\" }}", id);
            CloudQueueMessage message = new CloudQueueMessage(messageContent);

            await Fixture.TestQueue.AddMessageAsync(message);

            var resultBlob = Fixture.TestContainer.GetBlockBlobReference(id);
            string result = await TestHelpers.WaitForBlobAsync(resultBlob);
            Assert.Equal(TestHelpers.RemoveByteOrderMarkAndWhitespace(messageContent), TestHelpers.RemoveByteOrderMarkAndWhitespace(result));

            TraceEvent scriptTrace = Fixture.TraceWriter.Traces.SingleOrDefault(p => p.Message.Contains(id));
            Assert.Equal(TraceLevel.Verbose, scriptTrace.Level);

            string trace = TestHelpers.RemoveByteOrderMarkAndWhitespace(scriptTrace.Message);
            Assert.True(trace.Contains(TestHelpers.RemoveByteOrderMarkAndWhitespace("script processed queue message")));
            Assert.True(trace.Contains(TestHelpers.RemoveByteOrderMarkAndWhitespace(messageContent)));
        }

        protected async Task EasyTablesTest(bool writeToQueue = true)
        {
            // EasyTables needs the following environment vars:
            // "AzureWebJobsMobileAppUri" - the URI to the mobile app

            // The Mobile App needs an anonymous 'Item' EasyTable

            // First manually create an item. 
            string id = Guid.NewGuid().ToString();
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "input",  id }
            };
            await Fixture.Host.CallAsync("EasyTableOut", arguments);
            var item = await WaitForEasyTableRecordAsync("Item", id);

            Assert.Equal(item["id"], id);

            if (!writeToQueue)
            {
                return;
            }

            // Now add that Id to a Queue
            var queue = Fixture.GetNewQueue("easytables-input");
            await queue.AddMessageAsync(new CloudQueueMessage(id));

            // And wait for the text to be updated
            await WaitForEasyTableRecordAsync("Item", id, "This was updated!");
        }

        protected async Task<JToken> WaitForEasyTableRecordAsync(string tableName, string itemId, string textToMatch = null)
        {
            // Get the URI by creating a config.
            var config = new EasyTablesConfiguration();
            var client = new MobileServiceClient(config.MobileAppUri);
            JToken item = null;
            var table = client.GetTable(tableName);
            await TestHelpers.Await(() =>
            {
                bool result = false;
                try
                {
                    item = Task.Run(() => table.LookupAsync(itemId)).Result;
                    if (textToMatch != null)
                    {
                        result = item["Text"].ToString() == textToMatch;
                    }
                    else
                    {
                        result = true;
                    }
                }
                catch (AggregateException aggEx)
                {
                    var ex = (MobileServiceInvalidOperationException)aggEx.InnerException;
                    if (ex.Response.StatusCode != HttpStatusCode.NotFound)
                    {
                        throw;
                    }
                }

                return result;
            }, 10 * 1000);

            return item;
        }

        protected async Task WaitForTraceAsync()
        {
            await TestHelpers.Await(() =>
            {
                return Fixture.TraceWriter.Traces.Any(t => t.Message.Contains("Here."));
            });
        }

        protected static string RemoveByteOrderMarkAndWhitespace(string s)
        {
            string byteOrderMark = Encoding.UTF8.GetString(Encoding.UTF8.GetPreamble());
            return s.Trim().Replace(" ", string.Empty).Replace(byteOrderMark, string.Empty);
        }
    }
}