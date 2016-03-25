// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Diagnostics.Tracing;

namespace WebJobs.Script.WebHost.Diagnostics
{
    public static class MetricsEventManager
    {        
        private static FunctionActivityTracker instance = null;

        private static object functionActivityTrackerLockObject = new object();

        public static void FunctionStarted()
        {
            lock (functionActivityTrackerLockObject)
            {
                if (instance == null)
                {
                    instance = new FunctionActivityTracker();
                }
                instance.FunctionStarted();
            }
        }

        public static void FunctionCompleted()
        {
            lock (functionActivityTrackerLockObject)
            {
                if (instance != null)
                {
                    instance.FunctionCompleted();
                    if (!instance.IsActive)
                    {
                        instance.StopEtwTaskAndRaiseFinishedEvent();
                        instance = null;
                    }
                }
            }
        }

        private class FunctionActivityTracker : IDisposable
        {
            private readonly string executionId = Guid.NewGuid().ToString();
            private DateTime startTime = DateTime.UtcNow;
            private ulong totalExecutionCount = 0;
            private ulong runningFunctionCount = 0;
            private CancellationTokenSource etwTaskcancellationSource = new CancellationTokenSource();

            internal FunctionActivityTracker()
            {
                Task.Run(
                    async () =>
                    {
                        try
                        {
                            while (!etwTaskcancellationSource.Token.IsCancellationRequested)
                            {
                                RaiseMetricEtwEvent(ExecutionStage.InProgress);
                                await Task.Delay(TimeSpan.FromSeconds(5), etwTaskcancellationSource.Token);
                            }
                        }
                        catch (TaskCanceledException)
                        {
                            // This exception gets throws when cancellation request is raised via cancellation token.
                            // Let's eat this exception and continue
                        }
                    },
                    etwTaskcancellationSource.Token);
            }

            private enum ExecutionStage
            {
                InProgress,
                Finished
            }

            internal bool IsActive
            {
                get
                {
                    return runningFunctionCount != 0;
                }
            }

            protected virtual void Dispose(bool disposing)
            {
                if (disposing)
                {                    
                    etwTaskcancellationSource.Dispose();
                }
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

           internal void FunctionStarted()
           {
                totalExecutionCount++;
                runningFunctionCount++;
           }

            internal void FunctionCompleted()
            {
                if (runningFunctionCount > 0)
                {
                    runningFunctionCount--;
                }
            }

            internal void StopEtwTaskAndRaiseFinishedEvent()
            {
                etwTaskcancellationSource.Cancel();
                RaiseMetricEtwEvent(ExecutionStage.Finished);
            }

            private void RaiseMetricEtwEvent(ExecutionStage executionStage)
            {
                var timeSpan = (ulong)(DateTime.UtcNow - startTime).TotalMilliseconds;
                var executionCount = totalExecutionCount;
                WriteFunctionsMetricEvent(executionId, timeSpan, executionCount, executionStage.ToString());
            }

            private static void WriteFunctionsMetricEvent(string executionId, ulong executionTimeSpan, ulong executionCount, string executionStage)
            {
                MetricEventSource.Log.RaiseFunctionsMetricEvent(executionId, executionTimeSpan, executionCount, executionStage);
            }

            [EventSource(Guid = "08D0D743-5C24-43F9-9723-98277CEA5F9B")]
            private sealed class MetricEventSource : EventSource
            {
                internal static readonly MetricEventSource Log = new MetricEventSource();

                [Event(57906, Level = EventLevel.Informational, Channel = EventChannel.Operational)]
                public void RaiseFunctionsMetricEvent(string executionId, ulong executionTimeSpan, ulong executionCount, string executionStage)
                {
                    if (IsEnabled())
                    {
                        WriteEvent(57906, executionId, executionTimeSpan, executionCount, executionStage);
                    }
                }
            }
        }
    }
}