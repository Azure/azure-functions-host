// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging.Internal;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Xunit;

namespace Microsoft.Azure.WebJobs.Logging.FunctionalTests
{

    public class LoggerTest
    {
        static string CommonFuncName1 = "gamma";
       
        // End-2-end test that function instance counter can write to tables 
        [Fact]
        public async Task FunctionInstance()
        {
            var table = GetNewLoggingTable();
            try
            {
                ILogReader reader = LogFactory.NewReader(table);
                TimeSpan poll = TimeSpan.FromMilliseconds(50);
                TimeSpan poll5 = TimeSpan.FromMilliseconds(poll.TotalMilliseconds * 5);
                
                var logger1 = new CloudTableInstanceCountLogger("c1", table, 100) { PollingInterval = poll };
                
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
            finally
            {
                table.DeleteIfExists();
            }        
}

        [Fact]
        public async Task TimeRange()
        {
            // Make some very precise writes and verify we read exactly what we'd expect.

            var table = GetNewLoggingTable();
            try
            {
                ILogWriter writer = LogFactory.NewWriter("c1", table);
                ILogReader reader = LogFactory.NewReader(table);

                // Time that functios are called. 
                DateTime[] times = new DateTime[] {
                    new DateTime(2010, 3, 6, 10, 11, 20),
                    new DateTime(2010, 3, 7, 10, 11, 20),
                };
                DateTime tBefore0 = times[0].AddMinutes(-1);
                DateTime tAfter0= times[0].AddMinutes(1);

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

                await Verify(reader, DateTime.MinValue, tBefore0);

                await Verify(reader, DateTime.MinValue, tAfter0, logs[0]);
                await Verify(reader, DateTime.MinValue, tBefore1, logs[0]);

                await Verify(reader, DateTime.MinValue, tAfter1, logs[1], logs[0]);

                await Verify(reader, tAfter0, tBefore1); // inbetween, 0 

                await Verify(reader, tBefore1, tAfter1, logs[1]);
                await Verify(reader, tBefore1, DateTime.MaxValue, logs[1]);

            }
            finally
            {
                table.DeleteIfExists();
            }
        }

        // Verify that only the expected log items occur in the given window. 
        // logs should be sorted in reverse chronological order. 
        private async Task Verify(ILogReader reader, DateTime start, DateTime end, params FunctionInstanceLogItem[] expected)
        {
            var recent = await GetRecentAsync(reader, CommonFuncName1, start, end);
            Assert.Equal(expected.Length, recent.Length);

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i].FunctionInstanceId, recent[i].FunctionInstanceId);
            }
        }

        [Fact]
        public async Task LogExactWriteAndRead()
        {
            // Make some very precise writes and verify we read exactly what we'd expect.

            var table = GetNewLoggingTable();
            try
            {
                ILogWriter writer = LogFactory.NewWriter("c1", table);
                ILogReader reader = LogFactory.NewReader(table);

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
                var definitionSegment = await reader.GetFunctionDefinitionsAsync(null);
                string[] functionNames = Array.ConvertAll(definitionSegment.Results, definition => definition.Name);
                Array.Sort(functionNames);
                Assert.Equal(Func1, functionNames[0]);
                Assert.Equal(Func2, functionNames[1]);

                // Read Func1
                {
                    var segment1 = await reader.GetAggregateStatsAsync(Func1, DateTime.MinValue, DateTime.MaxValue, null);
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
                    var recent1 = await GetRecentAsync(reader, Func1);
                    Assert.Equal(2, recent1.Length);

                    Assert.Equal(recent1[0].FunctionInstanceId, l3.FunctionInstanceId); 
                    Assert.Equal(recent1[1].FunctionInstanceId, l1.FunctionInstanceId);
                }

                // Read Func2
                {
                    var segment2 = await reader.GetAggregateStatsAsync(Func2, DateTime.MinValue, DateTime.MaxValue, null);
                    var stats2 = segment2.Results;
                    Assert.Equal(1, stats2.Length);
                    Assert.Equal(stats2[0].TotalPass, 1);
                    Assert.Equal(stats2[0].TotalRun, 1);
                    Assert.Equal(stats2[0].TotalFail, 0);

                    var recent2 = await GetRecentAsync(reader, Func2);
                    Assert.Equal(1, recent2.Length);
                    Assert.Equal(recent2[0].FunctionInstanceId, l2.FunctionInstanceId);
                }           
            }
            finally
            {
                // Cleanup
                table.DeleteIfExists();
            }
        }

        static Task<IRecentFunctionEntry[]> GetRecentAsync(ILogReader reader, string functionName)
        {
            return GetRecentAsync(reader, functionName, DateTime.MinValue, DateTime.MaxValue);
        }

        static async Task<IRecentFunctionEntry[]> GetRecentAsync(ILogReader reader, string functionName, 
            DateTime start, DateTime end)
        {
            var query = await reader.GetRecentFunctionInstancesAsync(new RecentFunctionQuery
            {
                FunctionName = functionName,
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


        CloudTable GetNewLoggingTable()
        {
            string storageString = "AzureWebJobsDashboard";
            var acs = Environment.GetEnvironmentVariable(storageString);
            if (acs == null)
            {
                Assert.True(false, "Environment var " + storageString + " is not set. Should be set to an azure storage account connection string to use for testing.");
            }
            string tableName = "logtestXX" + Guid.NewGuid().ToString("n");

            CloudStorageAccount account = CloudStorageAccount.Parse(acs);
            var client = account.CreateCloudTableClient();
            var table = client.GetTableReference(tableName);
            table.CreateIfNotExists();

            return table;
        }

    }    
}
