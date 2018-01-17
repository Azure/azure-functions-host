// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public abstract class EndToEndTestsBase<TTestFixture> :
        IClassFixture<TTestFixture> where TTestFixture : EndToEndTestFixture, new()
    {
        private INameResolver _nameResolver = new DefaultNameResolver();
        private static readonly ScriptSettingsManager SettingsManager = ScriptSettingsManager.Instance;

        public EndToEndTestsBase(TTestFixture fixture)
        {
            Fixture = fixture;
        }

        protected TTestFixture Fixture { get; private set; }

        protected async Task TableInputTest()
        {
            TestHelpers.ClearFunctionLogs("TableIn");

            var input = new JObject
            {
                { "Region", "West" },
                { "Status", 1 }
            };
            var args = new Dictionary<string, object>()
            {
                { "input", input.ToString() }
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

            // verify input validation
            input = new JObject
            {
                { "Region", "West" },
                { "Status", "1 or Status neq 1" }
            };
            args = new Dictionary<string, object>()
            {
                { "input", input.ToString() }
            };
            var exception = await Assert.ThrowsAsync<FunctionInvocationException>(async () =>
            {
                await Fixture.Host.CallAsync("TableIn", args);
            });
            Assert.Equal("An invalid parameter value was specified for filter parameter 'Status'.", exception.InnerException.Message);
        }

        protected async Task TableOutputTest()
        {
            CloudTable table = Fixture.TableClient.GetTableReference("testoutput");
            await Fixture.DeleteEntities(table);

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

            var results = await table.ExecuteQuerySegmentedAsync(tableQuery, null);
            var entities = results.ToArray();
            Assert.Equal(3, entities.Length);

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

        public async Task ManualTrigger_Invoke_SucceedsTest()
        {
            TestHelpers.ClearFunctionLogs("ManualTrigger");

            string testData = Guid.NewGuid().ToString();
            string inputName = "input";
            if (Fixture.FixtureId == "powershell")
            {
                inputName = "triggerInput";
            }
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { inputName, testData }
            };
            await Fixture.Host.CallAsync("ManualTrigger", arguments);

            // make sure the input string made it all the way through
            var logs = await TestHelpers.GetFunctionLogsAsync("ManualTrigger");
            Assert.True(logs.Any(p => p.Contains(testData)), string.Join(Environment.NewLine, logs));
        }

        public async Task FileLogging_SucceedsTest()
        {
            string functionName = "Scenarios";
            TestHelpers.ClearFunctionLogs(functionName);

            string guid1 = Guid.NewGuid().ToString();
            string guid2 = Guid.NewGuid().ToString();

            ScenarioInput input = new ScenarioInput
            {
                Scenario = "fileLogging",
                Container = "scenarios-output",
                Value = $"{guid1};{guid2}"
            };
            Dictionary<string, object> arguments = new Dictionary<string, object>
                {
                    { "input", JsonConvert.SerializeObject(input) }
                };

            await Fixture.Host.CallAsync(functionName, arguments);

            // wait for logs to flush
            await Task.Delay(FileWriter.LogFlushIntervalMs);

            IList<string> logs = null;
            await TestHelpers.Await(() =>
            {
                logs = TestHelpers.GetFunctionLogsAsync(functionName, throwOnNoLogs: false).Result;
                return logs.Count > 0;
            });

            Assert.True(logs.Count == 4, string.Join(Environment.NewLine, logs));

            // No need for assert; this will throw if there's not one and only one
            logs.Single(p => p.EndsWith($"From TraceWriter: {guid1}"));
            logs.Single(p => p.EndsWith($"From ILogger: {guid2}"));
        }

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

            LogMessage traceEvent = await WaitForTraceAsync(p => p?.FormattedMessage != null && p.FormattedMessage.Contains(id));
            Assert.Equal(LogLevel.Information, traceEvent.Level);

            string trace = traceEvent.FormattedMessage;
            Assert.Contains("script processed queue message", trace);
            Assert.Contains(messageContent.Replace(" ", string.Empty), trace.Replace(" ", string.Empty));
        }

        protected async Task TwilioReferenceInvokeSucceedsImpl(bool isDotNet)
        {
            if (isDotNet)
            {
                TestHelpers.ClearFunctionLogs("TwilioReference");

                string testData = Guid.NewGuid().ToString();
                string inputName = "input";
                Dictionary<string, object> arguments = new Dictionary<string, object>
                {
                    { inputName, testData }
                };
                await Fixture.Host.CallAsync("TwilioReference", arguments);

                // make sure the input string made it all the way through
                var logs = await TestHelpers.GetFunctionLogsAsync("TwilioReference");
                Assert.True(logs.Any(p => p.Contains(testData)));
            }
        }

        protected static bool VerifyNotificationHubExceptionMessage(Exception exception)
        {
            if ((exception.Source == "Microsoft.Azure.NotificationHubs")
                && exception.Message.Contains("notification has no target applications"))
            {
                // Expected if using NH without any registrations
                return true;
            }
            return false;
        }

        protected async Task<LogMessage> WaitForTraceAsync(Func<LogMessage, bool> filter)
        {
            LogMessage traceEvent = null;

            await TestHelpers.Await(() =>
            {
                traceEvent = Fixture.LoggerProvider.GetAllLogMessages().SingleOrDefault(filter);
                return traceEvent != null;
            });

            return traceEvent;
        }

        protected async Task<JObject> GetFunctionTestResult(string functionName)
        {
            string logEntry = null;

            try
            {
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
            }
            catch (Exception)
            {
                // Give a more detailed exception
                var logs = TestHelpers.GetFunctionLogsAsync(functionName, throwOnNoLogs: false).Result;
                if (logs != null)
                {
                    var all = string.Join("\r\n", logs);
                    throw new ApplicationException("Expected 'TestResult' output message in logs.\r\n" + all);
                }
                throw;
            }

            int idx = logEntry.IndexOf("{");
            logEntry = logEntry.Substring(idx);

            return JObject.Parse(logEntry);
        }

        public class ScenarioInput
        {
            [JsonProperty("scenario")]
            public string Scenario { get; set; }

            [JsonProperty("container")]
            public string Container { get; set; }

            [JsonProperty("value")]
            public string Value { get; set; }
        }
    }
}