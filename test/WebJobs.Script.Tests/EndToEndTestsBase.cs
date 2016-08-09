// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.ApiHub;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.ServiceBus.Messaging;
using Microsoft.WindowsAzure.MobileServices;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    [Trait("Category", "E2E")]
    public abstract class EndToEndTestsBase<TTestFixture> :
        IClassFixture<TTestFixture> where TTestFixture : EndToEndTestFixture, new()
    {
        private INameResolver _nameResolver = new DefaultNameResolver();

        public EndToEndTestsBase(TTestFixture fixture)
        {
            Fixture = fixture;
        }

        protected TTestFixture Fixture { get; private set; }

        protected async Task TableInputTest()
        {
            TestHelpers.ClearFunctionLogs("TableIn");

            var args = new Dictionary<string, object>()
            {
                { "input", "{ \"Region\": \"West\" }" }
            };
            await Fixture.Host.CallAsync("TableIn", args);

            var logs = await TestHelpers.GetFunctionLogsAsync("TableIn");
            string result = logs.Where(p => p.Contains("Result:")).Single();
            result = result.Substring(result.IndexOf('{'));

            // verify singleton binding
            JObject resultObject = JObject.Parse(result);
            JObject single = (JObject)resultObject["single"];
            Assert.Equal("AAA", (string)single["PartitionKey"]);
            Assert.Equal("001", (string)single["RowKey"]);

            // verify partition binding
            JArray partition = (JArray)resultObject["partition"];
            Assert.Equal(3, partition.Count);
            foreach (var entity in partition)
            {
                Assert.Equal("BBB", (string)entity["PartitionKey"]);
            }

            // verify query binding
            JArray query = (JArray)resultObject["query"];
            Assert.Equal(2, query.Count);
            Assert.Equal("003", (string)query[0]["RowKey"]);
            Assert.Equal("004", (string)query[1]["RowKey"]);
        }

        protected async Task TableOutputTest()
        {
            CloudTable table = Fixture.TableClient.GetTableReference("testoutput");
            Fixture.DeleteEntities(table);

            JObject item = new JObject()
            {
                { "partitionKey", "TestOutput" },
                { "rowKey", 1 },
                { "stringProp", "Mathew" },
                { "intProp", 123 },
                { "boolProp", true },
                { "guidProp", Guid.NewGuid() },
                { "floatProp", 68756.898 }
            };

            var args = new Dictionary<string, object>()
            {
                { "input", item.ToString() }
            };
            await Fixture.Host.CallAsync("TableOut", args);

            // read the entities and verify schema
            TableQuery tableQuery = new TableQuery();
            var entities = table.ExecuteQuery(tableQuery).ToArray();
            Assert.Equal(2, entities.Length);

            foreach (var entity in entities)
            {
                Assert.Equal(EdmType.String, entity.Properties["stringProp"].PropertyType);
                Assert.Equal(EdmType.Int32, entity.Properties["intProp"].PropertyType);
                Assert.Equal(EdmType.Boolean, entity.Properties["boolProp"].PropertyType);

                // Guids end up roundtripping as strings
                Assert.Equal(EdmType.String, entity.Properties["guidProp"].PropertyType);

                Assert.Equal(EdmType.Double, entity.Properties["floatProp"].PropertyType);
            }
        }

        [Fact]
        public async Task QueueTriggerToBlobTest()
        {
            TestHelpers.ClearFunctionLogs("QueueTriggerToBlob");

            string id = Guid.NewGuid().ToString();
            string messageContent = string.Format("{{ \"id\": \"{0}\" }}", id);
            CloudQueueMessage message = new CloudQueueMessage(messageContent);

            await Fixture.TestQueue.AddMessageAsync(message);

            var resultBlob = Fixture.TestOutputContainer.GetBlockBlobReference(id);
            string result = await TestHelpers.WaitForBlobAndGetStringAsync(resultBlob);
            Assert.Equal(TestHelpers.RemoveByteOrderMarkAndWhitespace(messageContent), TestHelpers.RemoveByteOrderMarkAndWhitespace(result));

            TraceEvent traceEvent = await WaitForTraceAsync(p => p.Message.Contains(id));
            Assert.Equal(TraceLevel.Info, traceEvent.Level);

            string trace = traceEvent.Message;
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

            // Now add that Id to a Queue, in an object to test binding
            var queue = Fixture.GetNewQueue("documentdb-input");
            string messageContent = string.Format("{{ \"documentId\": \"{0}\" }}", id);
            await queue.AddMessageAsync(new CloudQueueMessage(messageContent));

            // And wait for the text to be updated
            Document updatedDoc = await WaitForDocumentAsync(id, "This was updated!");

            Assert.Equal(updatedDoc.Id, doc.Id);
            Assert.NotEqual(doc.ETag, updatedDoc.ETag);
        }

        protected async Task ServiceBusQueueTriggerToBlobTestImpl()
        {
            var resultBlob = Fixture.TestOutputContainer.GetBlockBlobReference("completed");
            await resultBlob.DeleteIfExistsAsync();

            string id = Guid.NewGuid().ToString();
            JObject message = new JObject
            {
                { "count", 0 },
                { "id", id }
            };

            using (Stream stream = new MemoryStream())
            using (TextWriter writer = new StreamWriter(stream))
            {
                writer.Write(message.ToString());
                writer.Flush();
                stream.Position = 0;

                await Fixture.ServiceBusQueueClient.SendAsync(new BrokeredMessage(stream) { ContentType = "text/plain" });
            }

            // now wait for function to be invoked
            string result = await TestHelpers.WaitForBlobAndGetStringAsync(resultBlob);

            Assert.Equal(TestHelpers.RemoveByteOrderMarkAndWhitespace(id), TestHelpers.RemoveByteOrderMarkAndWhitespace(result));
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
                if ((ex.InnerException != null && VerifyNotificationHubExceptionMessage(ex.InnerException)) ||
                    (ex.InnerException != null & ex.InnerException.InnerException != null && VerifyNotificationHubExceptionMessage(ex.InnerException.InnerException)))
                {
                    //Expected if using NH without any registrations
                }
                else
                {
                    throw;
                }
            }
        }

        protected async Task MobileTablesTest(bool isDotNet = false)
        {
            // MobileApps needs the following environment vars:
            // "AzureWebJobsMobileAppUri" - the URI to the mobile app

            // The Mobile App needs an anonymous 'Item' table

            // First manually create an item. 
            string id = Guid.NewGuid().ToString();
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "input",  id }
            };
            await Fixture.Host.CallAsync("MobileTableOut", arguments);
            var item = await WaitForMobileTableRecordAsync("Item", id);

            Assert.Equal(item["id"], id);

            // Now add that Id to a Queue
            var queue = Fixture.GetNewQueue("mobiletables-input");
            string messageContent = string.Format("{{ \"recordId\": \"{0}\" }}", id);
            await queue.AddMessageAsync(new CloudQueueMessage(messageContent));

            // And wait for the text to be updated

            // Only .NET fully supports updating from input bindings. Others will
            // create a new item with -success appended to the id.
            // https://github.com/Azure/azure-webjobs-sdk-script/issues/49
            var idToCheck = id + (isDotNet ? string.Empty : "-success");
            var textToCheck = isDotNet ? "This was updated!" : null;
            await WaitForMobileTableRecordAsync("Item", idToCheck, textToCheck);
        }

        protected async Task ApiHubTest()
        {
            // ApiHub for dropbox is enabled if the AzureWebJobsDropBox environment variable is set.           
            // The format should be: Endpoint={endpoint};Scheme={scheme};AccessToken={accesstoken}
            // or to use the local file system the format should be: UseLocalFileSystem=true;Path={path}
            string apiHubConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsDropBox");

            if (string.IsNullOrEmpty(apiHubConnectionString))
            {
                throw new ApplicationException("Missing AzureWebJobsDropBox environment variable.");
            }

            string testBlob = "teste2e";
            string apiHubFile = "teste2e/test.txt";
            var resultBlob = Fixture.TestOutputContainer.GetBlockBlobReference(testBlob);
            resultBlob.DeleteIfExists();

            var root = ItemFactory.Parse(apiHubConnectionString);
            if (await root.FileExistsAsync(apiHubFile))
            {
                var file = root.GetFileReference(apiHubFile);
                await file.DeleteAsync();
            }

            // Test both writing and reading from ApiHubFile.
            // First, manually invoke a function that has an output binding to write to Dropbox.
            string testData = Guid.NewGuid().ToString();

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "input", testData },
            };
            await Fixture.Host.CallAsync("ApiHubFileSender", arguments);

            // Second, there's an ApiHubFile trigger which will write a blob. 
            // Once the blob is written, we know both sender & listener are working.
            // TODO: removing the BOM character from result.
            string result = (await TestHelpers.WaitForBlobAndGetStringAsync(resultBlob)).Remove(0, 1);

            Assert.Equal(testData, result);
        }

        protected async Task<JToken> WaitForMobileTableRecordAsync(string tableName, string itemId, string textToMatch = null)
        {
            // We know the tests are using the default INameResolver and this setting.
            var mobileAppUri = _nameResolver.Resolve("AzureWebJobs_TestMobileUri");
            var client = new MobileServiceClient(new Uri(mobileAppUri));
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
            });

            return item;
        }

        protected async Task<Document> WaitForDocumentAsync(string itemId, string textToMatch = null)
        {
            var docUri = UriFactory.CreateDocumentUri("ItemDb", "ItemCollection", itemId);

            // We know the tests are using the default INameResolver and the default setting.
            var connectionString = _nameResolver.Resolve("AzureWebJobsDocumentDBConnectionString");
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

                    if (textToMatch != null)
                    {
                        result = doc.GetPropertyValue<string>("text") == textToMatch;
                    }
                    else
                    {
                        result = true;
                    }
                }
                catch (Exception)
                {
                }

                return result;
            });

            return doc;
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

        protected async Task<TraceEvent> WaitForTraceAsync(Func<TraceEvent, bool> filter)
        {
            TraceEvent traceEvent = null;

            await TestHelpers.Await(() =>
            {
                traceEvent = Fixture.TraceWriter.Traces.SingleOrDefault(filter);
                return traceEvent != null;
            });

            return traceEvent;
        }

        protected async Task<JObject> GetFunctionTestResult(string functionName)
        {
            string logEntry = null;

            await TestHelpers.Await(() =>
            {
                // search the logs for token "TestResult:" and parse the following JSON
                var logs = TestHelpers.GetFunctionLogsAsync(functionName, throwOnNoLogs: false).Result;
                if (logs != null)
                {
                    logEntry = logs.SingleOrDefault(p => p.Contains("TestResult:"));
                }
                return logEntry != null;
            });

            int idx = logEntry.IndexOf("{");
            logEntry = logEntry.Substring(idx);

            return JObject.Parse(logEntry);
        }
    }
}