// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.WebJobs.Extensions.DocumentDB;
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

            string trace = scriptTrace.Message;
            Assert.True(trace.Contains("script processed queue message"));
            Assert.True(trace.Replace(" ", string.Empty).Contains(messageContent.Replace(" ", string.Empty)));
        }

        protected async Task DocumentDBTest()
        {
            // DocumentDB tests need the following environment vars:
            // "AzureWebJobsDocumentDBConnectionString" -- the connection string to the account
            string id = Guid.NewGuid().ToString();
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "input",  id }
            };
            await Fixture.Host.CallAsync("DocumentDBOut", arguments);

            Document doc = await WaitForDocumentAsync(id);

            Assert.Equal(doc.Id, id);
        }

        protected async Task NotificationHubTest(string functionName)
        {
            // NotificationHub tests need the following environment vars:
            // "AzureWebJobsNotificationHubsConnectionString" -- the connection string for NotificationHubs
            // "AzureWebJobsNotificationHubName"  -- NotificationHubName
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "input",  "Hello" }
            };

            try
            {
                //Only verifying the call succeeds. It is not possible to verify
                //actual push notificaiton is delivered as they are sent only to 
                //client applications that registered with NotificationHubs
                await Fixture.Host.CallAsync(functionName, arguments);
            }
            catch (Exception ex)
            {
                // Node: Check innerException, CSharp: check innerExcpetion.innerException
                if (VerifyNotificationHubExceptionMessage(ex.InnerException)
                    || VerifyNotificationHubExceptionMessage(ex.InnerException.InnerException))
                {
                    //Expected if using NH without any registrations
                }
                else
                {
                    throw;
                }
            }
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

        protected async Task<Document> WaitForDocumentAsync(string itemId)
        {
            var docUri = UriFactory.CreateDocumentUri("ItemDb", "ItemCollection", itemId);

            // Get the connection string via the config
            var connectionString = new DocumentDBConfiguration().ConnectionString;
            var builder = new DbConnectionStringBuilder();
            builder.ConnectionString = connectionString;
            var serviceUri = new Uri(builder["AccountEndpoint"].ToString());
            var client = new DocumentClient(serviceUri, builder["AccountKey"].ToString());

            Document doc = null;
            await TestHelpers.Await(() =>
            {
                bool result = false;
                try
                {
                    var response = Task.Run(() => client.ReadDocumentAsync(docUri)).Result;
                    doc = response.Resource;
                    result = true;
                }
                catch (Exception)
                {
                }

                return result;
            }, 10 * 1000);

            return doc;
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

        protected static bool VerifyNotificationHubExceptionMessage(Exception exception)
        {
            if ((exception.Source == "Microsoft.Azure.NotificationHubs")
                && exception.Message.Contains("notification has no target applications"))
            {
                //Expected if using NH without any registrations
                return true;
            }
            return false;
        }
    }
}