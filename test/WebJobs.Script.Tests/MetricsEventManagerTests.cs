// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Moq;
using WebJobs.Script.WebHost.Diagnostics;
using WebJobs.Script.WebHost.Models;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class MetricsEventManagerTests
    {
        private Random _randomNumberGenerator = new Random();
        private const int MinimumLongRunningDurationInMs = 2000;
        private const int MinimumRandomValueForLongRunningDurationInMs = MinimumLongRunningDurationInMs + MinimumLongRunningDurationInMs;

        [Fact]
        public async Task MetricsEventManager_BasicTest()
        {
            var argsList = new List<FunctionExecutionEventArguments>();
            var metricsLogger = CreateWebHostMetricsLoggerInstance(argsList);
            var taskList = new List<Task>();
            taskList.Add(ShortTestFunction(metricsLogger));
            taskList.Add(LongTestFunction(metricsLogger));

            await AwaitFunctionTasks(taskList);
            ValidateFunctionExecutionEventArgumentsList(argsList, 2);
        }

        [Fact]
        public async Task MetricsEventManager_MultipleConcurrentShortFunctionExecutions()
        {
            var argsList = new List<FunctionExecutionEventArguments>();
            var metricsLogger = CreateWebHostMetricsLoggerInstance(argsList);
            var taskList = new List<Task>();
            var concurrency = _randomNumberGenerator.Next(5, 100);
            for (int currentIndex = 0; currentIndex < concurrency; currentIndex++)
            {
                taskList.Add(ShortTestFunction(metricsLogger));
            }
            
            await AwaitFunctionTasks(taskList);
            ValidateFunctionExecutionEventArgumentsList(argsList, concurrency);
        }

        [Fact]
        public async Task MetricsEventManager_MultipleConcurrentLongFunctionExecutions()
        {
            var argsList = new List<FunctionExecutionEventArguments>();
            var metricsLogger = CreateWebHostMetricsLoggerInstance(argsList);
            var taskList = new List<Task>();
            var concurrency = _randomNumberGenerator.Next(5, 100);
            for (int currentIndex = 0; currentIndex < concurrency; currentIndex++)
            {
                taskList.Add(LongTestFunction(metricsLogger));
            }

            await AwaitFunctionTasks(taskList);
            ValidateFunctionExecutionEventArgumentsList(argsList, concurrency);
            
            // All event should have the same executionId
            var invalidArgsList = argsList.Where(e => e.ExecutionId != argsList[0].ExecutionId).ToList();
            Assert.True(invalidArgsList.Count == 0,
                string.Format("There are events with different execution id. List:{0} Invalid entries:{1}",
                    SerializeFunctionExecutionEventArguments(argsList),
                    SerializeFunctionExecutionEventArguments(invalidArgsList)));

            Assert.True(argsList.Count >= concurrency * 2,
                string.Format("Each function invocation should emit atleast two etw events. List:{0}", SerializeFunctionExecutionEventArguments(argsList)));

            var uniqueInvocationIds = argsList.Select(i => i.InvocationId).Distinct().ToList();
            // Each invocation should have atleast one 'InProgress' event
            var invalidInvocationIds = uniqueInvocationIds.Where(
                i => !argsList.Exists(arg => arg.InvocationId == i && arg.ExecutionStage == ExecutionStage.Finished.ToString())
                        || !argsList.Exists(arg => arg.InvocationId == i && arg.ExecutionStage == ExecutionStage.InProgress.ToString())).ToList();

            Assert.True(invalidInvocationIds.Count == 0,
                string.Format("Each invocation should have atleast one 'InProgress' event. Invalid invocation ids:{0} List:{1}",
                    string.Join(",", invalidInvocationIds),
                    SerializeFunctionExecutionEventArguments(argsList)));
        }

        [Fact]
        public async Task MetricsEventManager_MultipleConcurrentFunctions()
        {
            var argsList = new List<FunctionExecutionEventArguments>();
            var metricsLogger = CreateWebHostMetricsLoggerInstance(argsList);
            var taskList = new List<Task>();
            var concurrency = _randomNumberGenerator.Next(5, 100);
            for (int currentIndex = 0; currentIndex < concurrency; currentIndex++)
            {
                if (_randomNumberGenerator.Next(100) < 50 ? true : false)
                {
                    taskList.Add(ShortTestFunction(metricsLogger));
                }
                else
                {
                    taskList.Add(LongTestFunction(metricsLogger));
                }
            }

            await AwaitFunctionTasks(taskList);
            ValidateFunctionExecutionEventArgumentsList(argsList, concurrency);
        }

        [Fact]
        public async Task MetricsEventManager_NonParallelExecutionsShouldHaveDifferentExecutionId()
        {
            var argsList = new List<FunctionExecutionEventArguments>();
            var metricsLogger = CreateWebHostMetricsLoggerInstance(argsList);
            await ShortTestFunction(metricsLogger);
            // Let's make sure that the tracker is not running anymore
            await Task.Delay(TimeSpan.FromMilliseconds(MinimumRandomValueForLongRunningDurationInMs));

            await ShortTestFunction(metricsLogger);
            // Let's make sure that the tracker is not running anymore
            await Task.Delay(TimeSpan.FromMilliseconds(MinimumRandomValueForLongRunningDurationInMs));

            Assert.True(argsList[0].ExecutionId != argsList[argsList.Count - 1].ExecutionId, "Execution ids are same");
        }

        private static async Task AwaitFunctionTasks(List<Task> taskList)
        {
            Task.WaitAll(taskList.ToArray());

            // Let's make sure that the tracker is not running anymore
            await Task.Delay(TimeSpan.FromMilliseconds(MinimumRandomValueForLongRunningDurationInMs));
        }

        private static void ValidateFunctionExecutionEventArgumentsList(List<FunctionExecutionEventArguments> list, int noOfFuncExecutions)
        {
            FunctionExecutionEventArguments invalidElement = null;
            string errorMessage = null;
            
            Assert.True(
                ValidateFunctionExecutionEventArgumentsList(list, noOfFuncExecutions, out invalidElement, out errorMessage),
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

                        var minEventsExpected = Math.Floor((double)lastEvent.ExecutionTimeSpan / (double)MinimumLongRunningDurationInMs) - 2;
                        var maxEventsExpected = Math.Ceiling((double)lastEvent.ExecutionTimeSpan / (double)MinimumLongRunningDurationInMs) + 2;
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
                invalidElement = unprocessedEvents[0].EventArguments;
                errorMessage = string.Format("There are unprocessed events: {0}", SerializeFunctionExecutionEventArguments(unprocessedEvents.Select(e => e.EventArguments).ToList()));
                return false;
            }

            if (hashes.Count != noOfFuncExecutions)
            {
                invalidElement = unprocessedEvents[0].EventArguments;
                errorMessage = string.Format("No of finished events does not match with number of function executions: Expected:{0} Actual:{1}", noOfFuncExecutions, hashes.Count);
                return false;
            }

            return true;
        }

        private static WebHostMetricsLogger CreateWebHostMetricsLoggerInstance(List<FunctionExecutionEventArguments> functionExecutionEventArgumentsList)
        {
            var metricsEventGenerator = new Mock<IMetricsEventGenerator>();
            metricsEventGenerator
                .Setup(e => e.RaiseFunctionExecutionEvent(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<long>(),
                    It.IsAny<bool>()))
                .Callback(
                (string executionId, string siteName, int concurrency, string functionName, string invocationId, string executionStage, long executionTimeSpan, bool success) =>
                {
                    functionExecutionEventArgumentsList.Add(new FunctionExecutionEventArguments(executionId, siteName, concurrency, functionName, invocationId, executionStage, executionTimeSpan, success));
                });

            return new WebHostMetricsLogger(metricsEventGenerator.Object, MinimumLongRunningDurationInMs / 1000);
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

        private static string SerializeFunctionExecutionEventArguments(List<FunctionExecutionEventArguments> args)
        {
            var stringBuffer = new StringBuilder();
            for (int currentIndex = 0; currentIndex < args.Count; currentIndex++)
            {
                stringBuffer.AppendFormat("Element No:{0} Details:{1} \t", currentIndex, args[currentIndex].ToString());
            }
            return stringBuffer.ToString();
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
