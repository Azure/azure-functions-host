using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using static Microsoft.Azure.WebJobs.Script.Metrics.FunctionMonitor;

namespace Microsoft.Azure.WebJobs.Script.Metrics.Tests
{
    public class IFunctionContainerActivityExtensionsTests
    {
        private FunctionContainerActivity _testContainerActivity;
        private TestAnalyticsPublisher _testAnalyticsPublisher;
        private string _executionId;

        public IFunctionContainerActivityExtensionsTests()
        {
            _executionId = Guid.NewGuid().ToString();

            _testContainerActivity = new FunctionContainerActivity
            {
                FunctionContainerSizeInMb = 128,
                SiteName = "TestSite"
            };

            _testAnalyticsPublisher = new TestAnalyticsPublisher();
        }

        [Fact]
        public void CalculateFunctionExecutionUnits_NoExecutions()
        {
            // create a few memory snapshots
            var now = DateTime.UtcNow;
            for (int i = 0; i < 5; i++)
            {
                now = now.AddSeconds(1);
                _testContainerActivity.UpdateMemorySnapshotList(128 * 1024 * 1024, now, _testAnalyticsPublisher, bufferSeconds: 0);
            }

            Assert.Null(_testContainerActivity.ActiveFunctionActivities);
            Assert.Equal(5, _testContainerActivity.MemorySnapshots.Count);
            Assert.Equal(0, _testContainerActivity.FunctionExecutionTimeInMs);
            Assert.Equal(0, _testContainerActivity.FunctionExecutionCount);
            Assert.Equal(0, _testContainerActivity.FunctionExecutionUnits);

            CalculateAllFunctionExecutionUnits();

            Assert.Equal(0, _testContainerActivity.GetFunctionGbSecs());
            Assert.Equal(0, _testContainerActivity.FunctionExecutionUnits);
            Assert.Null(_testContainerActivity.ActiveFunctionActivities);
            Assert.Empty(_testContainerActivity.MemorySnapshots);

            Assert.Empty(_testAnalyticsPublisher.Errors);
            Assert.Empty(_testAnalyticsPublisher.Events);
        }

        [Fact]
        public void CalculateFunctionExecutionUnits_SingleExecution()
        {
            // simulate an execution starting 8 seconds ago
            long functionDuration = 8000;
            var now = DateTime.UtcNow.Subtract(TimeSpan.FromMilliseconds(functionDuration));
            var testFunctionActivity = new FunctionActivity
            {
                ProcessId = 1234,
                ExecutionId = _executionId,
                InvocationId = Guid.NewGuid().ToString(),
                Concurrency = 1,
                FunctionName = "TestFunction",
                CurrentExecutionStage = FunctionExecutionStage.Finished,
                ExecutionTimeSpanInMs = functionDuration,
                StartTime = now,
                IsSucceeded = true
            };

            // simulate time moving forward 8 seconds and complete the execution
            now = now.AddMilliseconds(functionDuration);
            _testContainerActivity.UpdateFunctionActivity(testFunctionActivity, _testAnalyticsPublisher, now: now);

            Assert.Equal(functionDuration, testFunctionActivity.ExecutionTimeSpanInMs);
            Assert.Equal(functionDuration, _testContainerActivity.FunctionExecutionTimeInMs);

            // add a snapshot after the function completed
            now = now.AddMilliseconds(500);
            _testContainerActivity.UpdateMemorySnapshotList(128 * 1024 * 1024, now, _testAnalyticsPublisher, bufferSeconds: 0);

            Assert.Equal(1, _testContainerActivity.ActiveFunctionActivities.Count);
            Assert.Single(_testContainerActivity.MemorySnapshots);
            Assert.Equal(1 * functionDuration, _testContainerActivity.FunctionExecutionTimeInMs);
            Assert.Equal(_testContainerActivity.ActiveFunctionActivities.Values.Sum(p => p.ExecutionTimeSpanInMs), _testContainerActivity.FunctionExecutionTimeInMs);
            Assert.Equal(1, _testContainerActivity.FunctionExecutionCount);
            Assert.Equal(0, _testContainerActivity.FunctionExecutionUnits);
            Assert.Equal(default(DateTime), _testContainerActivity.LastFunctionExecutionCalcuationMemorySnapshotTime);
            var snapshotTime = _testContainerActivity.MemorySnapshots.Keys[0];

            CalculateAllFunctionExecutionUnits();

            // expect (128/1024) * 8 = 1 Gb-s
            // validate calculations and post state
            Assert.Equal(1, _testContainerActivity.GetFunctionGbSecs());
            Assert.Equal(1024000, _testContainerActivity.FunctionExecutionUnits);
            Assert.Equal(0, _testContainerActivity.ActiveFunctionActivities.Count);
            Assert.Empty(_testContainerActivity.MemorySnapshots);
            Assert.Equal(snapshotTime, _testContainerActivity.LastFunctionExecutionCalcuationMemorySnapshotTime);

            Assert.Empty(_testAnalyticsPublisher.Errors);
            var evt = Assert.Single(_testAnalyticsPublisher.Events);
            string expected = $"TestSite Functions ExecutionId {_executionId} ExecutionTimeSpan,ExecutionCount,ActualExecutionTimeSpan,FunctionContainerSize,FunctionName,InvocationId,Concurrency,IsSucceeded,StartTime,TotalDynamicMemoryBucketBilledTime,FunctionExecutionUnits,Reason,DynamicMemoryBucketCalculations 8000,1,8000,128,TestFunction,{testFunctionActivity.InvocationId},1,True";
            Assert.StartsWith(expected, evt);
        }

        [Fact]
        public void CalculateFunctionExecutionUnits_SingleExecution_Incomplete()
        {
            // simulate an execution starting 8 seconds ago,
            // leaving it in progress
            long functionDuration = 8000;
            var now = DateTime.UtcNow.Subtract(TimeSpan.FromMilliseconds(functionDuration));
            var testFunctionActivity = new FunctionActivity
            {
                ProcessId = 1234,
                ExecutionId = _executionId,
                InvocationId = Guid.NewGuid().ToString(),
                Concurrency = 1,
                FunctionName = "TestFunction",
                CurrentExecutionStage = FunctionExecutionStage.InProgress,
                ExecutionTimeSpanInMs = functionDuration,
                StartTime = now,
                IsSucceeded = true
            };
            // simulate time moving forward 8 seconds and record the in progress execution
            now = now.AddMilliseconds(functionDuration);
            _testContainerActivity.UpdateFunctionActivity(testFunctionActivity, _testAnalyticsPublisher, now: now);

            Assert.Equal(functionDuration, testFunctionActivity.ExecutionTimeSpanInMs);
            Assert.Equal(functionDuration, _testContainerActivity.FunctionExecutionTimeInMs);

            // add a snapshot
            _testContainerActivity.UpdateMemorySnapshotList(128 * 1024 * 1024, now, _testAnalyticsPublisher, bufferSeconds: 0);

            Assert.Equal(1, _testContainerActivity.ActiveFunctionActivities.Count);
            Assert.Single(_testContainerActivity.MemorySnapshots);
            Assert.Equal(1 * functionDuration, _testContainerActivity.FunctionExecutionTimeInMs);
            Assert.Equal(_testContainerActivity.ActiveFunctionActivities.Values.Sum(p => p.ExecutionTimeSpanInMs), _testContainerActivity.FunctionExecutionTimeInMs);
            Assert.Equal(0, _testContainerActivity.FunctionExecutionCount);
            Assert.Equal(0, _testContainerActivity.FunctionExecutionUnits);
            Assert.Equal(default(DateTime), _testContainerActivity.LastFunctionExecutionCalcuationMemorySnapshotTime);
            var snapshotTime = _testContainerActivity.MemorySnapshots.Keys[0];

            CalculateAllFunctionExecutionUnits();

            // expect (128/1024) * 8 = 1 Gb-s
            // validate calculations and post state
            Assert.Equal(1, _testContainerActivity.GetFunctionGbSecs());
            Assert.Equal(0, _testContainerActivity.FunctionExecutionCount);
            Assert.Equal(1024000, _testContainerActivity.FunctionExecutionUnits);
            Assert.Equal(1, _testContainerActivity.ActiveFunctionActivities.Count);
            Assert.Empty(_testContainerActivity.MemorySnapshots);
            Assert.Equal(snapshotTime, _testContainerActivity.LastFunctionExecutionCalcuationMemorySnapshotTime);

            Assert.Empty(_testAnalyticsPublisher.Errors);
            Assert.Empty(_testAnalyticsPublisher.Events);

            // now allow the function to execute a bit more then complete it
            testFunctionActivity.CurrentExecutionStage = FunctionExecutionStage.Finished;
            testFunctionActivity.ExecutionTimeSpanInMs += 2000;
            testFunctionActivity.IsSucceeded = true;
            now = now.AddMilliseconds(2000);
            _testContainerActivity.UpdateFunctionActivity(testFunctionActivity, _testAnalyticsPublisher, now: now);
            Assert.Equal(1, _testContainerActivity.FunctionExecutionCount);
            Assert.Equal(testFunctionActivity.ExecutionTimeSpanInMs, _testContainerActivity.FunctionExecutionTimeInMs);

            // add a snapshot
            now = now.AddMilliseconds(100);
            _testContainerActivity.UpdateMemorySnapshotList(128 * 1024 * 1024, now, _testAnalyticsPublisher, bufferSeconds: 0);

            Assert.Equal(1, _testContainerActivity.ActiveFunctionActivities.Count);
            Assert.Single(_testContainerActivity.MemorySnapshots);

            CalculateAllFunctionExecutionUnits();

            // verify that the remaining portion of the execution was
            // metered properly
            // since the remaining 2000ms is 1/4 of the previous 8000, we expect
            // the units to be increased by 25%.
            Assert.Equal(1.25, _testContainerActivity.GetFunctionGbSecs());
            Assert.Equal(1280000, _testContainerActivity.FunctionExecutionUnits);
            Assert.Equal(0, _testContainerActivity.ActiveFunctionActivities.Count);
            Assert.Empty(_testContainerActivity.MemorySnapshots);

            Assert.Empty(_testAnalyticsPublisher.Errors);
            Assert.Single(_testAnalyticsPublisher.Events);
        }

        [Fact]
        public async Task CalculateFunctionExecutionUnits_MultipleSequentialExecutions()
        {
            // run for 3 seconds
            Task memoryUpdateTask = Task.Run(async () =>
            {
                for (int i = 0; i < 12; i++)
                {
                    // todo: how should concurrency be set here?
                    _testContainerActivity.UpdateMemorySnapshotList(25 * 1024 * 1024, DateTime.UtcNow, _testAnalyticsPublisher, bufferSeconds: 0);
                    await Task.Delay(250);
                }
            });

            // interleave some function executions
            object syncLock = new object();
            Task functionExecutionsTask = Task.Run(async () =>
            {
                for (int i = 0; i < 30; i++)
                {
                    var testFunctionActivity = new FunctionActivity
                    {
                        ProcessId = 1234,
                        ExecutionId = _executionId,
                        InvocationId = Guid.NewGuid().ToString(),
                        Concurrency = 1,
                        FunctionName = "TestFunction",
                        CurrentExecutionStage = FunctionExecutionStage.InProgress,
                        StartTime = DateTime.UtcNow,
                        IsSucceeded = true
                    };
                    lock (syncLock)
                    {
                        _testContainerActivity.UpdateFunctionActivity(testFunctionActivity, _testAnalyticsPublisher);
                    }

                    // after a short delay complete the execution
                    int executionTimeMs = 100;
                    await Task.Delay(executionTimeMs);
                    testFunctionActivity.CurrentExecutionStage = FunctionExecutionStage.Finished;
                    testFunctionActivity.ExecutionTimeSpanInMs = (long)executionTimeMs;
                    lock (syncLock)
                    {
                        _testContainerActivity.UpdateFunctionActivity(testFunctionActivity, _testAnalyticsPublisher);
                    }
                }
            });

            await Task.WhenAll(memoryUpdateTask, functionExecutionsTask);

            // add one final bounding snapshot
            _testContainerActivity.UpdateMemorySnapshotList(25 * 1024 * 1024, DateTime.UtcNow, _testAnalyticsPublisher, bufferSeconds: 0);

            Assert.Equal(30, _testContainerActivity.ActiveFunctionActivities.Count);
            Assert.Equal(12 + 1, _testContainerActivity.MemorySnapshots.Count);
            Assert.Equal(30 * 100, _testContainerActivity.FunctionExecutionTimeInMs);
            Assert.Equal(_testContainerActivity.ActiveFunctionActivities.Values.Sum(p => p.ExecutionTimeSpanInMs), _testContainerActivity.FunctionExecutionTimeInMs);
            Assert.Equal(30, _testContainerActivity.FunctionExecutionCount);
            Assert.Equal(0, _testContainerActivity.FunctionExecutionUnits);

            CalculateAllFunctionExecutionUnits();

            // validate calculations and post state
            Assert.Equal(0.375, _testContainerActivity.GetFunctionGbSecs());
            Assert.Equal(384000, _testContainerActivity.FunctionExecutionUnits);
            Assert.Equal(0, _testContainerActivity.ActiveFunctionActivities.Count);
            Assert.Empty(_testContainerActivity.MemorySnapshots);

            Assert.Empty(_testAnalyticsPublisher.Errors);
            Assert.Equal(30, _testAnalyticsPublisher.Events.Count);
        }

        [Fact]
        public async Task CalculateFunctionExecutionUnits_MultipleConcurrentExecutions()
        {
            int numExecutions = 5000;
            int functionDuration = 1000;
            List<Task> functionExcutionTasks = new List<Task>();
            object syncLock = new object();
            for (int i = 0; i < numExecutions; i++)
            {
                var executionTask = Task.Run(async () =>
                {
                    var testFunctionActivity = new FunctionActivity
                    {
                        ProcessId = 1234,
                        ExecutionId = _executionId,
                        InvocationId = Guid.NewGuid().ToString(),
                        Concurrency = 1,
                        FunctionName = "TestFunction",
                        CurrentExecutionStage = FunctionExecutionStage.InProgress,
                        StartTime = DateTime.UtcNow,
                        IsSucceeded = true
                    };
                    lock (syncLock)
                    {
                        _testContainerActivity.UpdateFunctionActivity(testFunctionActivity, _testAnalyticsPublisher);
                    }
                    
                    await Task.Delay(functionDuration);
                    testFunctionActivity.CurrentExecutionStage = FunctionExecutionStage.Finished;
                    testFunctionActivity.ExecutionTimeSpanInMs = (long)functionDuration;
                    lock (syncLock)
                    {
                        _testContainerActivity.UpdateFunctionActivity(testFunctionActivity, _testAnalyticsPublisher);
                    }
                });
                functionExcutionTasks.Add(executionTask);
            }

            // add a few incomplete executions - expect these to be excluded
            int numIncompleteExecutions = 5;
            for (int i = 0; i < numIncompleteExecutions; i++)
            {
                var executionTask = Task.Run(() =>
                {
                    var testFunctionActivity = new FunctionActivity
                    {
                        ProcessId = 1234,
                        ExecutionId = _executionId,
                        InvocationId = Guid.NewGuid().ToString(),
                        Concurrency = 1,
                        FunctionName = "TestFunction",
                        CurrentExecutionStage = FunctionExecutionStage.InProgress,
                        StartTime = DateTime.UtcNow,
                        IsSucceeded = true
                    };
                    lock (syncLock)
                    {
                        _testContainerActivity.UpdateFunctionActivity(testFunctionActivity, _testAnalyticsPublisher);
                    }
                });
                functionExcutionTasks.Add(executionTask);
            }

            await Task.WhenAll(functionExcutionTasks);

            // add the bounding memory snapshot
            _testContainerActivity.UpdateMemorySnapshotList(512 * 1024 * 1024, DateTime.UtcNow, _testAnalyticsPublisher, bufferSeconds: 0);

            Assert.Equal(numExecutions + numIncompleteExecutions, _testContainerActivity.ActiveFunctionActivities.Count);
            Assert.Single(_testContainerActivity.MemorySnapshots);
            Assert.Equal((numExecutions * functionDuration) + (numIncompleteExecutions * IFunctionContainerActivityExtensions.MinimumExecutionTimespan), _testContainerActivity.FunctionExecutionTimeInMs);
            Assert.Equal(_testContainerActivity.ActiveFunctionActivities.Values.Sum(p => p.ExecutionTimeSpanInMs), _testContainerActivity.FunctionExecutionTimeInMs);
            Assert.Equal(numExecutions, _testContainerActivity.FunctionExecutionCount);
            Assert.Equal(0, _testContainerActivity.FunctionExecutionUnits);

            CalculateAllFunctionExecutionUnits();

            // validate calculations and post state
            Assert.Equal(625.0625, _testContainerActivity.GetFunctionGbSecs());
            Assert.Equal(640064000, _testContainerActivity.FunctionExecutionUnits);
            Assert.Equal(numIncompleteExecutions, _testContainerActivity.ActiveFunctionActivities.Count);
            Assert.Empty(_testContainerActivity.MemorySnapshots);

            Assert.Empty(_testAnalyticsPublisher.Errors);
            Assert.Equal(numExecutions, _testAnalyticsPublisher.Events.Count);
        }

        private void CalculateAllFunctionExecutionUnits()
        {
            while (_testContainerActivity.CalculateNextFunctionExecutionUnits(_testAnalyticsPublisher, buffer: false));
        }

        private class TestAnalyticsPublisher : IAnalyticsPublisher
        {
            public List<string> Errors { get; } = new List<string>();
            public List<string> Events { get; } = new List<string>();

            public void WriteError(int processId, string containerName, string message, string details)
            {
                Errors.Add($"{processId} {containerName} {message} {details}");
            }

            public void WriteEvent(string siteName = null, string feature = null, string objectTypes = null, string objectNames = null, string dataKeys = null, string dataValues = null, string action = null, DateTime? actionTimeStamp = null, bool succeeded = true)
            {
                Events.Add($"{siteName} {feature} {objectTypes} {objectNames} {dataKeys} {dataValues} {action}");
            }
        }
    }
}
