// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Metrics;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Moq;
using Moq.Protected;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class MetricsEventManagerTests
    {
        private const int MinimumLongRunningDurationInMs = 2000;
        private const int MinimumRandomValueForLongRunningDurationInMs = MinimumLongRunningDurationInMs + MinimumLongRunningDurationInMs;
        private readonly Random _randomNumberGenerator = new Random();
        private readonly MetricsEventManager _metricsEventManager;
        private readonly WebHostMetricsLogger _metricsLogger;
        private readonly List<FunctionExecutionEventArguments> _functionExecutionEventArguments;
        private readonly List<SystemMetricEvent> _events;

        public MetricsEventManagerTests()
        {
            _functionExecutionEventArguments = new List<FunctionExecutionEventArguments>();

            var mockEventGenerator = new Mock<IEventGenerator>();
            mockEventGenerator.Setup(e => e.LogFunctionExecutionEvent(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<long>(),
                    It.IsAny<bool>()))
                .Callback((string executionId, string siteName, int concurrency, string functionName, string invocationId, string executionStage, long executionTimeSpan, bool success) =>
                {
                    _functionExecutionEventArguments.Add(new FunctionExecutionEventArguments(executionId, siteName, concurrency, functionName, invocationId, executionStage, executionTimeSpan, success));
                });

            _events = new List<SystemMetricEvent>();
            mockEventGenerator.Setup(p => p.LogFunctionMetricEvent(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<long>(),
                    It.IsAny<long>(),
                    It.IsAny<long>(),
                    It.IsAny<long>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<string>()))
                .Callback((string subscriptionId, string appName, string functionName, string eventName, long average, long min, long max, long count, DateTime eventTimestamp, string data) =>
                {
                    var evt = new SystemMetricEvent
                    {
                        FunctionName = functionName,
                        EventName = eventName,
                        Average = average,
                        Minimum = min,
                        Maximum = max,
                        Count = count,
                        Data = data
                    };
                    _events.Add(evt);
                });

            var mockMetricsPublisher = new Mock<IMetricsPublisher>();
            _metricsEventManager = new MetricsEventManager(new TestEnvironment(), mockEventGenerator.Object, MinimumLongRunningDurationInMs / 1000, mockMetricsPublisher.Object);
            _metricsLogger = new WebHostMetricsLogger(_metricsEventManager);
        }

        [Fact]
        public void LogEvent_QueuesPendingEvent()
        {
            _metricsLogger.LogEvent("Event1");

            Assert.Equal(1, _metricsEventManager.QueuedEvents.Count);
            SystemMetricEvent evt = _metricsEventManager.QueuedEvents.Values.Single();
            Assert.Equal("event1", evt.EventName);  // case is normalized to lower
            Assert.Equal(1, evt.Count);
            Assert.Equal(0, evt.Average);
            Assert.Equal(0, evt.Minimum);
            Assert.Equal(0, evt.Maximum);

            _metricsLogger.LogEvent("Event2");
            _metricsLogger.LogEvent("Event3");

            Assert.Equal(3, _metricsEventManager.QueuedEvents.Count);
        }

        [Fact]
        public void LogEvent_AggregatesIntoExistingEvent()
        {
            SystemMetricEvent initialEvent = new SystemMetricEvent
            {
                EventName = "Event1",
                Count = 1
            };

            _metricsEventManager.QueuedEvents[initialEvent.EventName] = initialEvent;
            Assert.Equal(1, _metricsEventManager.QueuedEvents.Count);

            for (int i = 0; i < 10; i++)
            {
                _metricsLogger.LogEvent("Event1");

                Assert.Equal(1, _metricsEventManager.QueuedEvents.Count);

                // verify the new event was aggregated into the existing
                Assert.Equal(2 + i, initialEvent.Count);
            }

            Assert.Equal(1, _metricsEventManager.QueuedEvents.Count);
        }

        [Fact]
        public void LogEvent_Function_AggregatesIntoExistingEvent()
        {
            Assert.Equal(0, _metricsEventManager.QueuedEvents.Count);

            for (int i = 0; i < 10; i++)
            {
                _metricsLogger.LogEvent("Event1", "Function1");
            }

            for (int i = 0; i < 5; i++)
            {
                _metricsLogger.LogEvent("Event1", "Function2");
            }

            for (int i = 0; i < 15; i++)
            {
                _metricsLogger.LogEvent("Event3");
            }

            Assert.Equal(3, _metricsEventManager.QueuedEvents.Count);

            string key = MetricsEventManager.GetAggregateKey("Event1", "Function1");
            var metricEvent = _metricsEventManager.QueuedEvents[key];
            Assert.Equal(10, metricEvent.Count);
            Assert.Equal("event1", metricEvent.EventName);
            Assert.Equal("Function1", metricEvent.FunctionName);

            key = MetricsEventManager.GetAggregateKey("Event1", "Function2");
            metricEvent = _metricsEventManager.QueuedEvents[key];
            Assert.Equal(5, metricEvent.Count);
            Assert.Equal("event1", metricEvent.EventName);
            Assert.Equal("Function2", metricEvent.FunctionName);

            key = MetricsEventManager.GetAggregateKey("Event3");
            metricEvent = _metricsEventManager.QueuedEvents[key];
            Assert.Equal(15, metricEvent.Count);
            Assert.Equal("event3", metricEvent.EventName);
            Assert.Null(metricEvent.FunctionName);
        }

        [Fact]
        public void BeginEvent_ReturnsEventHandle()
        {
            object eventHandle = _metricsLogger.BeginEvent("Event1");

            SystemMetricEvent evt = (SystemMetricEvent)eventHandle;
            Assert.Equal("event1", evt.EventName);
            Assert.True((DateTime.UtcNow - evt.Timestamp).TotalSeconds < 15);
        }

        [Fact]
        public void EndEvent_CompletesPendingEvent()
        {
            object eventHandle = _metricsLogger.BeginEvent("Event1");

            Thread.Sleep(25);
            _metricsLogger.EndEvent(eventHandle);
            Assert.Equal(1, _metricsEventManager.QueuedEvents.Count);

            SystemMetricEvent evt = _metricsEventManager.QueuedEvents.Values.Single();
            Assert.Equal("event1", evt.EventName);
            Assert.Equal(1, evt.Count);
            Assert.True(evt.Maximum > 0);
            Assert.True(evt.Minimum > 0);
            Assert.True(evt.Average > 0);
        }

        [Fact]
        public void LatencyEvent_CompletesPendingEvent()
        {
            using (_metricsLogger.LatencyEvent("Event1"))
            {
                Assert.Equal(0, _metricsEventManager.QueuedEvents.Count);
            }

            Assert.Equal(1, _metricsEventManager.QueuedEvents.Count);
            var evt = _metricsEventManager.QueuedEvents.Single().Value;
            Assert.Equal("event1", evt.EventName);
            Assert.Equal(1, evt.Count);
        }

        [Fact]
        public void EndEvent_AggregatesIntoExistingEvent()
        {
            long prevMin = 123;
            long prevMax = 456;
            long prevAvg = 789;
            SystemMetricEvent initialEvent = new SystemMetricEvent
            {
                EventName = "Event1",
                Minimum = prevMin,
                Maximum = prevMax,
                Average = prevAvg,
                Count = 1
            };

            _metricsEventManager.QueuedEvents[initialEvent.EventName] = initialEvent;
            Assert.Equal(1, _metricsEventManager.QueuedEvents.Count);

            for (int i = 0; i < 10; i++)
            {
                SystemMetricEvent latencyEvent = (SystemMetricEvent)_metricsLogger.BeginEvent("Event1");
                Thread.Sleep(50);
                _metricsLogger.EndEvent(latencyEvent);

                Assert.Equal(1, _metricsEventManager.QueuedEvents.Count);

                // verify the new event was aggregated into the existing
                Assert.Equal(2 + i, initialEvent.Count);
                long latencyMS = (long)latencyEvent.Duration.TotalMilliseconds;
                Assert.Equal(initialEvent.Average, prevAvg + latencyMS);
                Assert.Equal(initialEvent.Minimum, Math.Min(prevMin, latencyMS));
                Assert.Equal(initialEvent.Maximum, Math.Max(prevMax, latencyMS));
                prevMin = initialEvent.Minimum;
                prevMax = initialEvent.Maximum;
                prevAvg += latencyMS;
            }

            Assert.Equal(1, _metricsEventManager.QueuedEvents.Count);
        }

        [Fact]
        public void EndEvent_Function_AggregatesIntoExistingEvent()
        {
            SystemMetricEvent metricEvent;
            for (int i = 0; i < 10; i++)
            {
                metricEvent = (SystemMetricEvent)_metricsLogger.BeginEvent("Event1", "Function1");
                Thread.Sleep(50);
                _metricsLogger.EndEvent(metricEvent);
            }

            for (int i = 0; i < 5; i++)
            {
                metricEvent = (SystemMetricEvent)_metricsLogger.BeginEvent("Event1", "Function2");
                Thread.Sleep(50);
                _metricsLogger.EndEvent(metricEvent);
            }

            for (int i = 0; i < 15; i++)
            {
                metricEvent = (SystemMetricEvent)_metricsLogger.BeginEvent("Event2");
                Thread.Sleep(50);
                _metricsLogger.EndEvent(metricEvent);
            }

            Assert.Equal(3, _metricsEventManager.QueuedEvents.Count);

            string key = MetricsEventManager.GetAggregateKey("Event1", "Function1");
            metricEvent = _metricsEventManager.QueuedEvents[key];
            Assert.Equal(10, metricEvent.Count);
            Assert.Equal("event1", metricEvent.EventName);
            Assert.Equal("Function1", metricEvent.FunctionName);

            key = MetricsEventManager.GetAggregateKey("Event1", "Function2");
            metricEvent = _metricsEventManager.QueuedEvents[key];
            Assert.Equal(5, metricEvent.Count);
            Assert.Equal("event1", metricEvent.EventName);
            Assert.Equal("Function2", metricEvent.FunctionName);

            key = MetricsEventManager.GetAggregateKey("Event2");
            metricEvent = _metricsEventManager.QueuedEvents[key];
            Assert.Equal(15, metricEvent.Count);
            Assert.Equal("event2", metricEvent.EventName);
            Assert.Null(metricEvent.FunctionName);
        }

        [Fact]
        public void EndEvent_InvalidHandle_NoOp()
        {
            Assert.Equal(0, _metricsEventManager.QueuedEvents.Count);

            _metricsLogger.EndEvent(new object());

            Assert.Equal(0, _metricsEventManager.QueuedEvents.Count);
        }

        [Fact]
        public async Task TimerFlush_MultipleEventsQueued_EmitsExpectedEvents()
        {
            object eventHandle = _metricsLogger.BeginEvent("Event1");
            _metricsLogger.LogEvent("Event2");
            _metricsLogger.LogEvent("Event2");
            _metricsLogger.LogEvent("Event2");
            _metricsLogger.LogEvent("Event3");
            _metricsLogger.LogEvent("Event4", "Function1");
            _metricsLogger.LogEvent("Event4", "Function2");
            _metricsLogger.LogEvent("Event4", "Function2");
            _metricsLogger.LogEvent("Event4", "Function2");
            _metricsLogger.LogEvent("Event5", "Function1");
            _metricsLogger.LogEvent("Event5", "Function1");
            _metricsLogger.LogEvent("Event5", "Function2", "TestData1");
            await Task.Delay(25);
            _metricsLogger.EndEvent(eventHandle);

            var latencyEvent = _metricsLogger.BeginEvent("Event6", "Function1");
            await Task.Delay(25);
            _metricsLogger.EndEvent(latencyEvent);
            latencyEvent = _metricsLogger.BeginEvent("Event6", "Function1");
            await Task.Delay(25);
            _metricsLogger.EndEvent(latencyEvent);
            latencyEvent = _metricsLogger.BeginEvent("Event6", "Function2", "TestData2");
            await Task.Delay(25);
            _metricsLogger.EndEvent(latencyEvent);

            int expectedEventCount = 9;
            Assert.Equal(expectedEventCount, _metricsEventManager.QueuedEvents.Count);

            _metricsEventManager.TimerFlush(null);

            Assert.Equal(expectedEventCount, _events.Count());

            SystemMetricEvent evt = _events.Single(p => p.EventName == "event1");
            Assert.True(evt.Average > 0);
            Assert.True(evt.Minimum > 0);
            Assert.True(evt.Maximum > 0);
            Assert.Equal(1, evt.Count);

            evt = _events.Single(p => p.EventName == "event2");
            Assert.Equal(3, evt.Count);

            evt = _events.Single(p => p.EventName == "event3");
            Assert.Equal(1, evt.Count);

            evt = _events.Single(p => p.EventName == "event4" && p.FunctionName == "Function1");
            Assert.Equal(1, evt.Count);

            evt = _events.Single(p => p.EventName == "event4" && p.FunctionName == "Function2");
            Assert.Equal(3, evt.Count);

            evt = _events.Single(p => p.EventName == "event5" && p.FunctionName == "Function1");
            Assert.Equal(2, evt.Count);
            Assert.Equal(evt.Data, string.Empty);

            evt = _events.Single(p => p.EventName == "event5" && p.FunctionName == "Function2");
            Assert.Equal(1, evt.Count);
            Assert.Equal(evt.Data, "TestData1");

            evt = _events.Single(p => p.EventName == "event6" && p.FunctionName == "Function1");
            Assert.True(evt.Average > 0);
            Assert.True(evt.Minimum > 0);
            Assert.True(evt.Maximum > 0);
            Assert.Equal(2, evt.Count);

            evt = _events.Single(p => p.EventName == "event6" && p.FunctionName == "Function2");
            Assert.True(evt.Average > 0);
            Assert.True(evt.Minimum > 0);
            Assert.True(evt.Maximum > 0);
            Assert.Equal(evt.Data, "TestData2");
            Assert.Equal(1, evt.Count);

            Assert.Equal(0, _metricsEventManager.QueuedEvents.Count);
        }

        [Fact]
        public async Task TimerFlush_CalledOnExpectedInterval()
        {
            int flushInterval = 10;
            Mock<IEventGenerator> mockGenerator = new Mock<IEventGenerator>();
            Mock<MetricsEventManager> mockEventManager = new Mock<MetricsEventManager>(new TestEnvironment(), mockGenerator.Object, flushInterval, null, flushInterval) { CallBase = true };
            MetricsEventManager eventManager = mockEventManager.Object;

            int numFlushes = 0;
            mockEventManager.Protected().Setup("TimerFlush", ItExpr.IsAny<object>())
                .Callback<object>((state) =>
                {
                    numFlushes++;
                });

            // here we're just verifying that we're called multiple times
            await TestHelpers.Await(() => numFlushes >= 5, timeout: 2000, pollingInterval: 100, userMessageCallback: () => $"Expected numFlushes >= 5; Actual: {numFlushes}");

            mockEventManager.VerifyAll();
        }

        [Fact]
        public void Dispose_FlushesQueuedEvents()
        {
            _metricsLogger.LogEvent("Event1");
            _metricsLogger.LogEvent("Event2");
            _metricsLogger.LogEvent("Event2");
            _metricsLogger.LogEvent("Event3");

            Assert.Equal(3, _metricsEventManager.QueuedEvents.Count);

            _metricsEventManager.Dispose();

            Assert.Equal(3, _events.Count());
            Assert.Equal(0, _metricsEventManager.QueuedEvents.Count);
        }

        [Fact]
        public async Task MetricsEventManager_BasicTest()
        {
            var taskList = new List<Task>();
            taskList.Add(ShortTestFunction(_metricsLogger));
            taskList.Add(LongTestFunction(_metricsLogger));

            await AwaitFunctionTasks(taskList);
            ValidateFunctionExecutionEventArgumentsList(_functionExecutionEventArguments, 2);
        }

        [Fact]
        public async Task MetricsEventManager_MultipleConcurrentShortFunctionExecutions()
        {
            var taskList = new List<Task>();
            var concurrency = _randomNumberGenerator.Next(5, 100);
            for (int currentIndex = 0; currentIndex < concurrency; currentIndex++)
            {
                taskList.Add(ShortTestFunction(_metricsLogger));
            }

            await AwaitFunctionTasks(taskList);
            ValidateFunctionExecutionEventArgumentsList(_functionExecutionEventArguments, concurrency);
        }

        [Fact]
        public async Task MetricsEventManager_MultipleConcurrentLongFunctionExecutions()
        {
            var taskList = new List<Task>();
            var concurrency = _randomNumberGenerator.Next(5, 100);
            for (int currentIndex = 0; currentIndex < concurrency; currentIndex++)
            {
                taskList.Add(LongTestFunction(_metricsLogger));
            }

            await AwaitFunctionTasks(taskList);
            ValidateFunctionExecutionEventArgumentsList(_functionExecutionEventArguments, concurrency);

            // All events should have the same executionId
            var invalidArgsList = _functionExecutionEventArguments.Where(e => e.ExecutionId != _functionExecutionEventArguments[0].ExecutionId).ToList();
            Assert.True(invalidArgsList.Count == 0,
                string.Format("There are events with different execution ids. List:{0} Invalid entries:{1}",
                    SerializeFunctionExecutionEventArguments(_functionExecutionEventArguments),
                    SerializeFunctionExecutionEventArguments(invalidArgsList)));

            Assert.True(_functionExecutionEventArguments.Count >= concurrency * 2,
                string.Format("Each function invocation should emit atleast two etw events. List:{0}", SerializeFunctionExecutionEventArguments(_functionExecutionEventArguments)));

            var uniqueInvocationIds = _functionExecutionEventArguments.Select(i => i.InvocationId).Distinct().ToList();

            // Each invocation should have atleast one 'InProgress' event
            var invalidInvocationIds = uniqueInvocationIds.Where(
                i => !_functionExecutionEventArguments.Exists(arg => arg.InvocationId == i && arg.ExecutionStage == ExecutionStage.Finished.ToString())
                        || !_functionExecutionEventArguments.Exists(arg => arg.InvocationId == i && arg.ExecutionStage == ExecutionStage.InProgress.ToString())).ToList();

            Assert.True(invalidInvocationIds.Count == 0,
                string.Format("Each invocation should have atleast one 'InProgress' event. Invalid invocation ids:{0} List:{1}",
                    string.Join(",", invalidInvocationIds),
                    SerializeFunctionExecutionEventArguments(_functionExecutionEventArguments)));
        }

        [Fact]
        public async Task MetricsEventManager_MultipleConcurrentFunctions()
        {
            var taskList = new List<Task>();
            var concurrency = _randomNumberGenerator.Next(5, 100);
            for (int currentIndex = 0; currentIndex < concurrency; currentIndex++)
            {
                if (_randomNumberGenerator.Next(100) < 50 ? true : false)
                {
                    taskList.Add(ShortTestFunction(_metricsLogger));
                }
                else
                {
                    taskList.Add(LongTestFunction(_metricsLogger));
                }
            }

            await AwaitFunctionTasks(taskList);
            ValidateFunctionExecutionEventArgumentsList(_functionExecutionEventArguments, concurrency);
        }

        [Fact]
        public async Task MetricsEventManager_NonParallelExecutionsShouldHaveDifferentExecutionId()
        {
            await ShortTestFunction(_metricsLogger);

            // Let's make sure that the tracker is not running anymore
            await Task.Delay(TimeSpan.FromMilliseconds(MinimumRandomValueForLongRunningDurationInMs));

            await ShortTestFunction(_metricsLogger);

            // Let's make sure that the tracker is not running anymore
            await Task.Delay(TimeSpan.FromMilliseconds(MinimumRandomValueForLongRunningDurationInMs));

            Assert.True(_functionExecutionEventArguments[0].ExecutionId != _functionExecutionEventArguments[_functionExecutionEventArguments.Count - 1].ExecutionId, "Execution ids are same");
        }

        [Theory]
        [InlineData("Event1", null, "event1")]
        [InlineData("Event1", "", "event1")]
        [InlineData("Event1", "Function1", "event1_function1")]
        public void GetAggregateKey_ReturnsExpectedValue(string eventName, string functionName, string expected)
        {
            string result = MetricsEventManager.GetAggregateKey(eventName, functionName);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void SystemEvent_DebugValue_ReturnsExpectedValue()
        {
            PropertyInfo debugValueProp = typeof(SystemMetricEvent).GetProperties(BindingFlags.Instance | BindingFlags.NonPublic).Single(p => p.Name == "DebugValue");

            var evt = new SystemMetricEvent
            {
                EventName = "Event1",
                Count = 123
            };
            Assert.Equal("(Event: Event1, Count: 123)", debugValueProp.GetValue(evt));

            evt.FunctionName = "Function1";
            Assert.Equal("(Function: Function1, Event: Event1, Count: 123)", debugValueProp.GetValue(evt));
        }

        private static async Task AwaitFunctionTasks(List<Task> taskList)
        {
            Task.WaitAll(taskList.ToArray());

            // Let's make sure that the tracker is not running anymore
            await Task.Delay(TimeSpan.FromMilliseconds(MinimumRandomValueForLongRunningDurationInMs));
        }

        private static void ValidateFunctionExecutionEventArgumentsList(List<FunctionExecutionEventArguments> list, int noOfFuncExecutions)
        {

            Assert.True(
                ValidateFunctionExecutionEventArgumentsList(list, noOfFuncExecutions, out FunctionExecutionEventArguments invalidElement, out string errorMessage),
                string.Format("ErrorMessage:{0} InvalidElement:{1} List:{2}", errorMessage, invalidElement.ToString(), SerializeFunctionExecutionEventArguments(list)));
        }

        private static bool ValidateFunctionExecutionEventArgumentsList(List<FunctionExecutionEventArguments> list, int noOfFuncExecutions, out FunctionExecutionEventArguments invalidElement, out string errorMessage)
        {
            invalidElement = new FunctionExecutionEventArguments();
            errorMessage = string.Empty;
            var functionValidationTrackerList = new List<FunctionEventValidationTracker<FunctionExecutionEventArguments>>();
            for (int currentIndex = 0; currentIndex < list.Count; currentIndex++)
            {
                functionValidationTrackerList.Add(new FunctionEventValidationTracker<FunctionExecutionEventArguments>(list[currentIndex]));
            }

            var hashes = new HashSet<string>();
            for (int currentIndex = 0; currentIndex < functionValidationTrackerList.Count; currentIndex++)
            {
                // The element has not already been processed
                if (!functionValidationTrackerList[currentIndex].HasBeenProcessed)
                {
                    var functionExecutionArgs = functionValidationTrackerList[currentIndex].EventArguments;

                    if (hashes.Contains(functionExecutionArgs.InvocationId))
                    {
                        invalidElement = functionExecutionArgs;
                        errorMessage = "InvocationId has already been used";
                        return false;
                    }

                    // If function execution was in progress then there should be a corresponding 'Finished' event
                    if (functionExecutionArgs.ExecutionStage == ExecutionStage.InProgress.ToString())
                    {
                        List<int> relatedEventIds = new List<int>();
                        relatedEventIds.Add(currentIndex);
                        for (int secondIndex = currentIndex + 1; secondIndex < functionValidationTrackerList.Count; secondIndex++)
                        {
                            // The element has not already been processed for another function execution and related to the current function invocation event
                            if (!functionValidationTrackerList[secondIndex].HasBeenProcessed
                                && functionValidationTrackerList[secondIndex].EventArguments.FunctionName == functionExecutionArgs.FunctionName
                                && functionValidationTrackerList[secondIndex].EventArguments.InvocationId == functionExecutionArgs.InvocationId)
                            {
                                relatedEventIds.Add(secondIndex);
                                if (functionValidationTrackerList[secondIndex].EventArguments.ExecutionStage == ExecutionStage.Finished.ToString())
                                {
                                    break;
                                }
                            }
                        }

                        if (relatedEventIds.Count < 2)
                        {
                            invalidElement = functionExecutionArgs;
                            errorMessage = "There should be atleast one related event";
                            return false;
                        }

                        var lastEvent = list[relatedEventIds[relatedEventIds.Count - 1]];
                        if (lastEvent.ExecutionStage != ExecutionStage.Finished.ToString())
                        {
                            invalidElement = lastEvent;
                            errorMessage = "Couldn't find Finished event for the current function invocation";
                            return false;
                        }
                        else
                        {
                            hashes.Add(lastEvent.InvocationId);
                        }

                        var minEventsExpected = Math.Floor(lastEvent.ExecutionTimeSpan / (double)MinimumLongRunningDurationInMs) - 2;
                        var maxEventsExpected = Math.Ceiling(lastEvent.ExecutionTimeSpan / (double)MinimumLongRunningDurationInMs) + 2;

                        // We should see atleast one InProgress event if it takes more than 5 seconds
                        if (lastEvent.ExecutionTimeSpan >= MinimumLongRunningDurationInMs
                            && (relatedEventIds.Count < minEventsExpected
                            || relatedEventIds.Count > maxEventsExpected))
                        {
                            invalidElement = lastEvent;
                            errorMessage = string.Format("Long running function doesn't contain expected number of Etw events. Minimum:{0} Maximum:{1} Actual:{2}", minEventsExpected, maxEventsExpected, relatedEventIds.Count);
                            return false;
                        }

                        foreach (var relatedEventId in relatedEventIds)
                        {
                            // Mark all related events as processed
                            functionValidationTrackerList[relatedEventId].HasBeenProcessed = true;
                        }
                    }
                    else if (functionExecutionArgs.ExecutionStage == ExecutionStage.Finished.ToString())
                    {
                        functionValidationTrackerList[currentIndex].HasBeenProcessed = true;
                        hashes.Add(functionExecutionArgs.InvocationId);
                    }
                }
            }

            var unprocessedEvents = functionValidationTrackerList.Where(e => !e.HasBeenProcessed).ToList();
            if (unprocessedEvents.Count > 0)
            {
                invalidElement = unprocessedEvents.FirstOrDefault()?.EventArguments;
                errorMessage = string.Format("There are unprocessed events: {0}", SerializeFunctionExecutionEventArguments(unprocessedEvents.Select(e => e.EventArguments).ToList()));
                return false;
            }

            if (hashes.Count != noOfFuncExecutions)
            {
                invalidElement = unprocessedEvents.FirstOrDefault()?.EventArguments;
                errorMessage = string.Format("No of finished events does not match with number of function executions: Expected:{0} Actual:{1}", noOfFuncExecutions, hashes.Count);
                return false;
            }

            return true;
        }

        private async Task LongTestFunction(WebHostMetricsLogger metricsLogger)
        {
            var randomMilliSeconds = _randomNumberGenerator.Next(MinimumRandomValueForLongRunningDurationInMs, MinimumRandomValueForLongRunningDurationInMs * 4);
            await TestFunction(Guid.NewGuid().ToString(), Guid.NewGuid(), metricsLogger, TimeSpan.FromMilliseconds(randomMilliSeconds));
        }

        private async Task ShortTestFunction(WebHostMetricsLogger metricsLogger)
        {
            var randomMilliSeconds = _randomNumberGenerator.Next(0, 10);
            await TestFunction(Guid.NewGuid().ToString(), Guid.NewGuid(), metricsLogger, TimeSpan.FromMilliseconds(randomMilliSeconds));
        }

        private async Task TestFunction(WebHostMetricsLogger metricsLogger, TimeSpan waitTimeSpan)
        {
            await TestFunction(Guid.NewGuid().ToString(), Guid.NewGuid(), metricsLogger, waitTimeSpan);
        }

        private async Task TestFunction(string name, Guid invocationId, WebHostMetricsLogger metricsLogger, TimeSpan waitTimeSpan)
        {
            var functionMetadata = new FunctionMetadata
            {
                Name = name
            };
            var functionEvent = new FunctionStartedEvent(invocationId, functionMetadata);
            metricsLogger.BeginEvent(functionEvent);
            await Task.Delay(waitTimeSpan);
            metricsLogger.EndEvent(functionEvent);
        }

        private static string SerializeFunctionExecutionEventArguments(List<FunctionExecutionEventArguments> args)
        {
            var stringBuffer = new StringBuilder();
            for (int currentIndex = 0; currentIndex < args.Count; currentIndex++)
            {
                stringBuffer.AppendFormat("Element No:{0} Details:{1} \t", currentIndex, args[currentIndex].ToString());
            }
            return stringBuffer.ToString();
        }

        private class FunctionEventValidationTracker<T>
        {
            public FunctionEventValidationTracker(T eventArguments)
            {
                EventArguments = eventArguments;
                HasBeenProcessed = false;
            }

            public T EventArguments { get; set; }

            public bool HasBeenProcessed { get; set; }
        }

        private class FunctionExecutionEventArguments
        {
            internal FunctionExecutionEventArguments()
            {
            }

            internal FunctionExecutionEventArguments(string executionId, string siteName, int concurrency, string functionName, string invocationId, string executionStage, long executionTimeSpan, bool success)
            {
                ExecutionId = executionId;
                SiteName = siteName;
                Concurrency = concurrency;
                FunctionName = functionName;
                InvocationId = invocationId;
                ExecutionStage = executionStage;
                ExecutionTimeSpan = executionTimeSpan;
                Success = success;
            }

            internal string ExecutionId { get; private set; }

            internal string SiteName { get; private set; }

            internal int Concurrency { get; private set; }

            internal string FunctionName { get; private set; }

            internal string InvocationId { get; private set; }

            internal string ExecutionStage { get; private set; }

            internal long ExecutionTimeSpan { get; private set; }

            internal bool Success { get; private set; }

            public override string ToString()
            {
                return string.Format("ExecutionId:{0} SiteName:{1} Concurrency:{2} FunctionName:{3} InvocationId:{4} ExecutionStage:{5} ExecutionTimeSpan:{6} Success:{7}",
                    ExecutionId,
                    SiteName,
                    Concurrency,
                    FunctionName,
                    InvocationId,
                    ExecutionStage,
                    ExecutionTimeSpan,
                    Success);
            }
        }
    }
}