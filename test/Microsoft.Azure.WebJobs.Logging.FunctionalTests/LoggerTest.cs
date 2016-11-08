// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging.Internal;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Xunit;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Logging.FunctionalTests
{
    public class LoggerTest : IDisposable, ILogTableProvider
    {
        static string DefaultHost = "host";
        static string CommonFuncName1 = "gamma"; 
        static FunctionId CommonFuncId1 = FunctionId.Build(DefaultHost, CommonFuncName1); // default values

        private List<CloudTable> _tables = new List<CloudTable>();

        private string _tableNamePrefix = "logtestYY" + Guid.NewGuid().ToString("n");

        private CloudTableClient _tableClient;

        // Delete any tables we created. 
        public void Dispose()
        {
            foreach (var table in _tables)
            {
                table.DeleteIfExists();
            }
        }

        // End-2-end test that function instance counter can write to tables 
        [Fact] 
        public async Task FunctionInstance()
        {
            ILogReader reader = LogFactory.NewReader(this);
            TimeSpan poll = TimeSpan.FromMilliseconds(50);
            TimeSpan poll5 = TimeSpan.FromMilliseconds(poll.TotalMilliseconds * 5);

            var logger1 = new CloudTableInstanceCountLogger("c1", this, 100) { PollingInterval = poll };

            Guid g1 = Guid.NewGuid();

            DateTime startTime = DateTime.UtcNow;
            logger1.Increment(g1);
            await Task.Delay(poll5); // should get at least 1 poll entry in           
            logger1.Decrement(g1);
            await Task.WhenAll(logger1.StopAsync());

            DateTime endTime = DateTime.UtcNow;

            // Now read. 
            // We may get an arbitrary number of raw poll entries since the
            // low poll latency combined with network delay can be unpredictable.
            var values = await reader.GetVolumeAsync(startTime, endTime, 1);

            double totalVolume = (from value in values select value.Volume).Sum();
            Assert.True(totalVolume > 0);

            double totalInstance = (from value in values select value.InstanceCounts).Sum();
            Assert.Equal(1, totalInstance);
        }

        // Unit testing on function name normalization. 
        [Theory]
        [InlineData("abc123", "abc123")]
        [InlineData("ABC123", "abc123")] // case insensitive, normalizes to same value as lowercase. 
        [InlineData("abc-123", "abc:2D123")] // '-' is escaped 
        [InlineData("abc:2D123", "abc:3A2d123")] // double escape still works. Previous escaped values become lowercase.
        public void NormalizeFunctionName(string name, string expected)
        {
            var method = typeof(ILogWriter).Assembly.GetType("Microsoft.Azure.WebJobs.Logging.TableScheme").GetMethod("NormalizeFunctionName", BindingFlags.Static | BindingFlags.Public);
            Func<string, string> escape = (string val) => (string)method.Invoke(null, new object[] { val });
            string actual = escape(name);
            Assert.Equal(actual, expected);
        }

        [Fact] 
        public async Task ReadNoTable()
        {
            ILogReader reader = LogFactory.NewReader(this);
            Assert.Equal(0, this._tables.Count); // no tables yet. 
            
            var segmentDef = await reader.GetFunctionDefinitionsAsync(null, null);
            Assert.Equal(0, segmentDef.Results.Length);

            var segmentTimeline = await reader.GetActiveContainerTimelineAsync(DateTime.MinValue, DateTime.MaxValue, null);
            Assert.Equal(0, segmentTimeline.Results.Length);

            var segmentRecent = await reader.GetRecentFunctionInstancesAsync(new RecentFunctionQuery
            {
                FunctionId = FunctionId.Parse("abc"),
                Start = DateTime.MinValue,
                End = DateTime.MaxValue,
                MaximumResults = 1000
            }, null);
            Assert.Equal(0, segmentRecent.Results.Length);

            var item = await reader.LookupFunctionInstanceAsync(Guid.NewGuid());
            Assert.Null(item);
        }


        [Fact]
        public async Task TimeRange()
        {
            // Make some very precise writes and verify we read exactly what we'd expect.
            ILogWriter writer = LogFactory.NewWriter(DefaultHost, "c1", this);
            ILogReader reader = LogFactory.NewReader(this);

            // Time that functios are called. 
            DateTime[] times = new DateTime[] {
                    new DateTime(2010, 3, 6, 10, 11, 20),
                    new DateTime(2010, 3, 7, 10, 11, 20),
                };
            DateTime tBefore0 = times[0].AddMinutes(-1);
            DateTime tAfter0 = times[0].AddMinutes(1);

            DateTime tBefore1 = times[1].AddMinutes(-1);
            DateTime tAfter1 = times[1].AddMinutes(1);

            var logs = Array.ConvertAll(times, time => new FunctionInstanceLogItem
            {
                FunctionInstanceId = Guid.NewGuid(),
                FunctionName = CommonFuncName1,
                StartTime = time
            });

            var tasks = Array.ConvertAll(logs, log => WriteAsync(writer, log));
            await Task.WhenAll(tasks);
            await writer.FlushAsync();

            // Try various combinations. 
            await Verify(reader, DateTime.MinValue, DateTime.MaxValue, logs[1], logs[0]); // Infinite range, includes all.
            await Verify(reader, tBefore0, tAfter1, logs[1], logs[0]); //  barely hugs both instances

            await Verify(reader, DateTime.MinValue, tBefore0); // Empty 
            await Verify(reader, tAfter1, DateTime.MaxValue); // Empty 

            await Verify(reader, DateTime.MinValue, tAfter0, logs[0]);
            await Verify(reader, DateTime.MinValue, tBefore1, logs[0]);

            await Verify(reader, DateTime.MinValue, tAfter1, logs[1], logs[0]);

            await Verify(reader, tAfter0, tBefore1); // inbetween, 0 

            await Verify(reader, tBefore1, tAfter1, logs[1]);
            await Verify(reader, tBefore1, DateTime.MaxValue, logs[1]);
        }

        static DateTime After(DateTime t)
        {
            return t.AddMinutes(1);
        }
        static DateTime Before(DateTime t)
        {
            return t.AddMinutes(-1);
        }

        // Use time ranges that are far apart and will spawn multiple tables.
        [Fact]
        public async Task TimeRangeAcrossEpochs()
        {
            // Make some very precise writes and verify we read exactly what we'd expect.
            ILogWriter writer = LogFactory.NewWriter(DefaultHost, "c1", this);
            ILogReader reader = LogFactory.NewReader(this);

            // Time that functios are called. 
            DateTime[] times = new DateTime[] {                
                    // Epoch 37
                    new DateTime(2012, 3, 6, 10, 11, 20, DateTimeKind.Utc),
                    new DateTime(2012, 3, 7, 10, 11, 20, DateTimeKind.Utc),
                    
                    // consecutive Epoch 38
                    new DateTime(2012, 4, 8, 10, 11, 20, DateTimeKind.Utc),
                                        
                    // Skip to Epoch  41
                    new DateTime(2012, 7, 9, 10, 11, 20, DateTimeKind.Utc)
                };

            var logs = Array.ConvertAll(times, time => new FunctionInstanceLogItem
            {
                FunctionInstanceId = Guid.NewGuid(),
                FunctionName = CommonFuncName1,
                StartTime = time,
            });

            var tasks = Array.ConvertAll(logs, log => WriteAsync(writer, log));
            await Task.WhenAll(tasks);
            await writer.FlushAsync();

            // Test point lookups for individual function instances. 
            foreach (var log in logs)
            {
                var entry = await reader.LookupFunctionInstanceAsync(log.FunctionInstanceId);
                Assert.NotNull(entry);

                Assert.Equal(log.FunctionInstanceId, entry.FunctionInstanceId);
                Assert.Equal(log.FunctionName, entry.FunctionName);
                Assert.Equal(log.StartTime, entry.StartTime);
                Assert.Equal(log.EndTime, entry.EndTime);                
            }

            // Try various combinations. 
            await Verify(reader, DateTime.MinValue, DateTime.MaxValue, logs[3], logs[2], logs[1], logs[0]); // Infinite range, includes all.

            // Various combinations of straddling an epoch boundary 
            await Verify(reader, Before(times[1]), After(times[2]), logs[2], logs[1]); 
            await Verify(reader, Before(times[1]), Before(times[2]), logs[1]); 
            await Verify(reader, After(times[1]), Before(times[2]));

            // Skipping over an empty epoch 
            await Verify(reader, Before(times[1]), Before(times[3]), logs[2], logs[1]);

            // Now... delete the middle table; and verify the other data is still there. 
            ILogTableProvider provider = this;
            var table = provider.GetTable("201204"); 
            Assert.True(table.Exists());
            table.Delete();

            await Verify(reader, DateTime.MinValue, DateTime.MaxValue, logs[3], logs[1], logs[0]); // Infinite range, includes all.

            // function instance entry from the table we deleted is now missing.  
            var entry2 = await reader.LookupFunctionInstanceAsync(logs[2].FunctionInstanceId);
            Assert.Null(entry2);
        }

        // Verify that only the expected log items occur in the given window. 
        // logs should be sorted in reverse chronological order. 
        private async Task Verify(ILogReader reader, DateTime start, DateTime end, params FunctionInstanceLogItem[] expected)
        {
            var recent = await GetRecentAsync(reader, CommonFuncId1, start, end);
            Assert.Equal(expected.Length, recent.Length);

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i].FunctionInstanceId, recent[i].FunctionInstanceId);
            }
        }

        // Timestamp for incrementing when QuickWrite() happens. 
        private DateTime _quickTimestamp = new DateTime(2017, 3, 6, 10, 11, 20, DateTimeKind.Utc);

        // Write a function entry. Don't care about any other details.
        async Task<Guid> QuickWriteAsync(ILogWriter writer, string functionName)
        {
            
            FunctionInstanceLogItem l1 = new FunctionInstanceLogItem
            {
                FunctionInstanceId = Guid.NewGuid(),
                FunctionName = functionName,
                StartTime = _quickTimestamp,
                EndTime = _quickTimestamp.AddMinutes(1)
                // inferred as Running since no end time.
            };

            _quickTimestamp = _quickTimestamp.AddMinutes(2);

            await writer.AddAsync(l1);
            await writer.FlushAsync();

            return l1.FunctionInstanceId;
        }
       
        // Have 2 different host writers to the same storage; results should be different. 
        // This is testing that we're handling the host ids. 
        [Fact]
        public async Task DifferentHosts()
        {
            // 1a & 1b are 2 instances (different machines) of the same host. They share. 
            // 2 is a separate host. 
            string host1 = "h1-1"; // includes an tricky character that requires escaping. 
            string host2 = "h22";
            ILogWriter writer1a = LogFactory.NewWriter(host1, "c1", this);
            ILogWriter writer1b = LogFactory.NewWriter(host1, "c2", this);
            ILogWriter writer2 = LogFactory.NewWriter(host2, "c3", this);

            ILogReader reader1 = LogFactory.NewReader(this);
            ILogReader reader2 = LogFactory.NewReader(this);

            string Func1 = "alpha";

            var f1a = await QuickWriteAsync(writer1a, Func1); // first 
            var f1b = await QuickWriteAsync(writer1b, Func1);
            var f1aa = await QuickWriteAsync(writer1a, Func1); // second write
            var f2 = await QuickWriteAsync(writer2, Func1);

            // Verify readers 
            // Function definitions. Search all hosts if no host specified 
            {
                var segment = await reader1.GetFunctionDefinitionsAsync(null, null);

                Assert.Equal(2, segment.Results.Length);
                var allDefinitions = segment.Results;
                
                segment = await reader1.GetFunctionDefinitionsAsync(host1, null);

                Assert.Equal(1, segment.Results.Length);
                var host1Defs = segment.Results[0];
                Assert.Equal(Func1, host1Defs.Name);
                Assert.Equal(FunctionId.Build(host1, Func1), host1Defs.FunctionId);

                segment = await reader1.GetFunctionDefinitionsAsync(host2, null);

                Assert.Equal(1, segment.Results.Length);
                var host2Defs = segment.Results[0];
                Assert.Equal(Func1, host2Defs.Name);
                Assert.Equal(FunctionId.Build(host2, Func1), host2Defs.FunctionId);

                Assert.Equal(Func1, allDefinitions[0].Name);
                Assert.Equal(Func1, allDefinitions[1].Name);
                Assert.Equal(host1Defs.FunctionId, allDefinitions[0].FunctionId);
                Assert.Equal(host2Defs.FunctionId, allDefinitions[1].FunctionId);
            }            

            // Recent list 
            {
                var segment = await reader1.GetRecentFunctionInstancesAsync(new RecentFunctionQuery
                {
                    FunctionId = FunctionId.Build(host1, Func1),
                    End = DateTime.MaxValue, 
                }, null);
                Guid[] guids = Array.ConvertAll(segment.Results, x => x.FunctionInstanceId);

                Assert.Equal(3, guids.Length); // Only include host 1
                Assert.Equal(f1a, guids[2]); // reverse chronological 
                Assert.Equal(f1b, guids[1]);
                Assert.Equal(f1aa, guids[0]);
            }

            // cross polination. Lookup across hosts. 
            {
                var entry = await reader2.LookupFunctionInstanceAsync(f1a);
                Assert.NotNull(entry);
                Assert.Equal(entry.FunctionName, Func1);
            }

        }

        [Fact]
        public async Task LogStart()
        {
            // Make some very precise writes and verify we read exactly what we'd expect.
            ILogWriter writer = LogFactory.NewWriter(DefaultHost, "c1", this);
            ILogReader reader = LogFactory.NewReader(this);

            string Func1 = "alpha";

            var t1a = new DateTime(2010, 3, 6, 10, 11, 20, DateTimeKind.Utc);

            FunctionInstanceLogItem l1 = new FunctionInstanceLogItem
            {
                FunctionInstanceId = Guid.NewGuid(),
                FunctionName = Func1,
                StartTime = t1a,
                LogOutput = "one"
                // inferred as Running since no end time.
            };
            await writer.AddAsync(l1);

            await writer.FlushAsync();
            // Start event should exist. 

            var entries = await GetRecentAsync(reader, l1.FunctionId);
            Assert.Equal(1, entries.Length);
            Assert.Equal(entries[0].Status, FunctionInstanceStatus.Running);
            Assert.Equal(entries[0].EndTime, null);

            l1.EndTime = l1.StartTime.Add(TimeSpan.FromSeconds(1));
            l1.Status = FunctionInstanceStatus.CompletedSuccess;
            await writer.AddAsync(l1);

            await writer.FlushAsync();

            // Should overwrite the previous row. 

            entries = await GetRecentAsync(reader, l1.FunctionId);
            Assert.Equal(1, entries.Length);
            Assert.Equal(entries[0].Status, FunctionInstanceStatus.CompletedSuccess);
            Assert.Equal(entries[0].EndTime.Value.DateTime, l1.EndTime);
        }

        // Logs are case-insensitive, case-preserving
        [Fact]
        public async Task Casing()
        {
            // Make some very precise writes and verify we read exactly what we'd expect.
            ILogWriter writer = LogFactory.NewWriter(DefaultHost, "c1", this);
            ILogReader reader = LogFactory.NewReader(this);

            string FuncOriginal = "UPPER-lower";
            string Func2 = FuncOriginal.ToLower(); // casing permutations
            string Func3 = Func2.ToLower();

            var t1a = new DateTime(2010, 3, 6, 10, 11, 20, DateTimeKind.Utc);

            FunctionInstanceLogItem l1 = new FunctionInstanceLogItem
            {
                FunctionInstanceId = Guid.NewGuid(),
                FunctionName = FuncOriginal,
                StartTime = t1a,
                LogOutput = "one"
                // inferred as Running since no end time.
            };
            await writer.AddAsync(l1);

            await writer.FlushAsync();
            // Start event should exist. 


            var definitionSegment = await reader.GetFunctionDefinitionsAsync(null, null);
            Assert.Equal(1, definitionSegment.Results.Length);
            Assert.Equal(FuncOriginal, definitionSegment.Results[0].Name);

            // Lookup various casings 
            foreach (var name in new string[] { FuncOriginal, Func2, Func3 })
            {
                var entries = await GetRecentAsync(reader, l1.FunctionId);
                Assert.Equal(1, entries.Length);
                Assert.Equal(entries[0].Status, FunctionInstanceStatus.Running);
                Assert.Equal(entries[0].EndTime, null);
                Assert.Equal(entries[0].FunctionName, FuncOriginal); // preserving. 
            }
        }

        // Test that large output logs get truncated. 
        [Fact]
        public async Task LargeWritesAreTruncated()
        {
            ILogWriter writer = LogFactory.NewWriter(DefaultHost, "c1", this);
            ILogReader reader = LogFactory.NewReader(this);

            List<Guid> functionIds = new List<Guid>();

            // Max table request size is 4mb. That gives roughly 40kb per row. 
            string smallValue = new string('y', 100);
            string largeValue = new string('x', 100 * 1000);
            string truncatedPrefix = largeValue.Substring(0, 100);

            for (int i = 0; i < 90; i++)
            {
                var functionId = Guid.NewGuid();
                functionIds.Add(functionId);

                var now = DateTime.UtcNow;
                var item = new FunctionInstanceLogItem
                {
                    FunctionInstanceId = functionId,
                    Arguments = new Dictionary<string, string>
                    {
                        { "p1", largeValue },
                        { "p2", smallValue },
                        { "p3", smallValue },
                        { "p4", smallValue },
                        { "p5", null },
                        { "p6", "" }
                    },
                    StartTime = now,
                    EndTime = now.AddSeconds(3),
                    FunctionName = "tst2",
                    LogOutput = largeValue,
                    ErrorDetails = largeValue,
                    TriggerReason = largeValue
                };

                await writer.AddAsync(item);
            }

            // If we didn't truncate, then this would throw with a 413 "too large" exception. 
            await writer.FlushAsync();

            // If we got here without an exception, then we successfully truncated the rows. 

            // If we got here without an exception, then we successfully truncated the rows. 
            // Lookup and verify 
            var instance = await reader.LookupFunctionInstanceAsync(functionIds[0]);
            Assert.True(instance.LogOutput.StartsWith(truncatedPrefix));
            Assert.True(instance.ErrorDetails.StartsWith(truncatedPrefix));
            Assert.True(instance.TriggerReason.StartsWith(truncatedPrefix));

            Assert.Equal(6, instance.Arguments.Count);
            Assert.True(instance.Arguments["p1"].StartsWith(truncatedPrefix));
            Assert.Equal(smallValue, instance.Arguments["p2"]);
            Assert.Equal(smallValue, instance.Arguments["p3"]);
            Assert.Equal(smallValue, instance.Arguments["p4"]);
            Assert.Equal(null, instance.Arguments["p5"]);
            Assert.Equal("", instance.Arguments["p6"]);
        }

        // Test that large output logs getr truncated. 
        [Fact]
        public async Task LargeWritesWithParametersAreTruncated()
        {
            ILogWriter writer = LogFactory.NewWriter(DefaultHost, "c1", this);
            ILogReader reader = LogFactory.NewReader(this);

            // Max table request size is 4mb. That gives roughly 40kb per row. 
            string largeValue = new string('x', 100 * 1000);
            string truncatedPrefix = largeValue.Substring(0, 100);

            List<Guid> functionIds = new List<Guid>();
            for (int i = 0; i < 90; i++)
            {
                var functionId = Guid.NewGuid();
                functionIds.Add(functionId);
                var now = DateTime.UtcNow;
                var item = new FunctionInstanceLogItem
                {
                    FunctionInstanceId = functionId,
                    Arguments = new Dictionary<string, string>(),
                    StartTime = now,
                    EndTime = now.AddSeconds(3),
                    FunctionName = "tst2",
                    LogOutput = largeValue,
                    ErrorDetails = largeValue,
                    TriggerReason = largeValue
                };
                for (int j = 0; j < 1000; j++)
                {
                    string paramName = "p" + j.ToString();
                    item.Arguments[paramName] = largeValue;
                }

                await writer.AddAsync(item);
            }

            // If we didn't truncate, then this would throw with a 413 "too large" exception. 
            await writer.FlushAsync();

            // If we got here without an exception, then we successfully truncated the rows. 
            // Lookup and verify 
            var instance = await reader.LookupFunctionInstanceAsync(functionIds[0]);
            Assert.True(instance.LogOutput.StartsWith(truncatedPrefix));
            Assert.True(instance.ErrorDetails.StartsWith(truncatedPrefix));
            Assert.True(instance.TriggerReason.StartsWith(truncatedPrefix));

            Assert.Equal(0, instance.Arguments.Count); // totally truncated.           
        }

        [Fact]
        public async Task LogExactWriteAndRead()
        {
            // Make some very precise writes and verify we read exactly what we'd expect.
            ILogWriter writer = LogFactory.NewWriter(DefaultHost, "c1", this);
            ILogReader reader = LogFactory.NewReader(this);

            string Func1 = "alpha";
            string Func2 = "beta";

            var t1a = new DateTime(2010, 3, 6, 10, 11, 20);
            var t1b = new DateTime(2010, 3, 6, 10, 11, 21); // same time bucket as t1a
            var t2 = new DateTime(2010, 3, 7, 10, 11, 21);

            FunctionInstanceLogItem l1 = new FunctionInstanceLogItem
            {
                FunctionInstanceId = Guid.NewGuid(),
                FunctionName = Func1,
                StartTime = t1a,
                LogOutput = "one"
            };
            await WriteAsync(writer, l1);

            await writer.FlushAsync(); // Multiple flushes; test starting & stopping the backgrounf worker. 

            FunctionInstanceLogItem l2 = new FunctionInstanceLogItem
            {
                FunctionInstanceId = Guid.NewGuid(),
                FunctionName = Func2,
                StartTime = t1b,
                LogOutput = "two"
            };
            await WriteAsync(writer, l2);

            FunctionInstanceLogItem l3 = new FunctionInstanceLogItem
            {
                FunctionInstanceId = Guid.NewGuid(),
                FunctionName = Func1,
                StartTime = t2,
                LogOutput = "three",
                ErrorDetails = "this failed"
            };
            await WriteAsync(writer, l3);

            await writer.FlushAsync();

            // Now read 
            var definitionSegment = await reader.GetFunctionDefinitionsAsync(null, null);
            string[] functionNames = Array.ConvertAll(definitionSegment.Results, definition => definition.Name);
            Array.Sort(functionNames);
            Assert.Equal(Func1, functionNames[0]);
            Assert.Equal(Func2, functionNames[1]);

            // Read Func1
            {
                var segment1 = await reader.GetAggregateStatsAsync(l3.FunctionId, DateTime.MinValue, DateTime.MaxValue, null);
                Assert.Null(segment1.ContinuationToken);
                var stats1 = segment1.Results;
                Assert.Equal(2, stats1.Length); // includes t1 and t2

                // First bucket has l1, second bucket has l3
                Assert.Equal(stats1[0].TotalPass, 1);
                Assert.Equal(stats1[0].TotalRun, 1);
                Assert.Equal(stats1[0].TotalFail, 0);

                Assert.Equal(stats1[1].TotalPass, 0);
                Assert.Equal(stats1[1].TotalRun, 1);
                Assert.Equal(stats1[1].TotalFail, 1);

                // reverse order. So l3 latest function, is listed first. 
                var recent1 = await GetRecentAsync(reader, l3.FunctionId);
                Assert.Equal(2, recent1.Length);

                Assert.Equal(recent1[0].FunctionInstanceId, l3.FunctionInstanceId);
                Assert.Equal(recent1[1].FunctionInstanceId, l1.FunctionInstanceId);
            }

            // Read Func2
            {
                var segment2 = await reader.GetAggregateStatsAsync(l2.FunctionId, DateTime.MinValue, DateTime.MaxValue, null);
                var stats2 = segment2.Results;
                Assert.Equal(1, stats2.Length);
                Assert.Equal(stats2[0].TotalPass, 1);
                Assert.Equal(stats2[0].TotalRun, 1);
                Assert.Equal(stats2[0].TotalFail, 0);

                var recent2 = await GetRecentAsync(reader, l2.FunctionId);
                Assert.Equal(1, recent2.Length);
                Assert.Equal(recent2[0].FunctionInstanceId, l2.FunctionInstanceId);
            }
        }

        static Task<IRecentFunctionEntry[]> GetRecentAsync(ILogReader reader, FunctionId functionId)
        {
            return GetRecentAsync(reader, functionId, DateTime.MinValue, DateTime.MaxValue);
        }

        static async Task<IRecentFunctionEntry[]> GetRecentAsync(ILogReader reader, FunctionId functionId,
            DateTime start, DateTime end)
        {
            var query = await reader.GetRecentFunctionInstancesAsync(new RecentFunctionQuery
            {
                FunctionId = functionId,
                Start = start,
                End = end,
                MaximumResults = 1000
            }, null);
            var results = query.Results;
            return results;
        }

        static async Task WriteAsync(ILogWriter writer, FunctionInstanceLogItem item)
        {
            item.Status = FunctionInstanceStatus.Running;
            await writer.AddAsync(item); // Start

            if (item.ErrorDetails == null)
            {
                item.Status = FunctionInstanceStatus.CompletedSuccess;
            }
            else
            {
                item.Status = FunctionInstanceStatus.CompletedFailure;
            }
            item.EndTime = item.StartTime.AddSeconds(1);
            await writer.AddAsync(item); // end 
        }

        CloudTable ILogTableProvider.GetTable(string suffix)
        {
            lock(_tables)
            {
                var tableClient = GetTableClient();
                var tableName = _tableNamePrefix + "x" + suffix;
                var table = tableClient.GetTableReference(tableName);
                _tables.Add(table);
                return table;
            }
        }


         // List all tables that we may have handed out. 
        Task<CloudTable[]> ILogTableProvider.ListTablesAsync()
        {
            var tableClient = GetTableClient();
            var tables = tableClient.ListTables(_tableNamePrefix).ToArray();
            return Task.FromResult<CloudTable[]>(tables);
        }
                
        private CloudTableClient GetTableClient()
        {
            if (_tableClient == null)
            {
                string storageString = "AzureWebJobsDashboard";
                var acs = Environment.GetEnvironmentVariable(storageString);
                if (acs == null)
                {
                    Assert.True(false, "Environment var " + storageString + " is not set. Should be set to an azure storage account connection string to use for testing.");
                }

                CloudStorageAccount account = CloudStorageAccount.Parse(acs);
                _tableClient = account.CreateCloudTableClient();                
            }
            return _tableClient;
        }
    }
}
