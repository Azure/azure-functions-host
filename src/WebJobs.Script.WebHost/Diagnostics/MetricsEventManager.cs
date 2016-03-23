// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
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

        public static void HostStartedEvent(ScriptHost scriptHost)
        {
            if (scriptHost == null
                || scriptHost.Functions == null)
            {
                return;
            }

            var siteName = GetNormalizedString(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));

            foreach (var function in scriptHost.Functions)
            {
                if (function == null)
                {
                    continue;
                }
                string fileExtension = null;
                if (function.Metadata != null
                    && !string.IsNullOrEmpty(function.Metadata.Source))
                {
                    fileExtension = Path.GetExtension(function.Metadata.Source);
                    if (!string.IsNullOrEmpty(fileExtension))
                    {
                        fileExtension = fileExtension.ToLower(CultureInfo.InvariantCulture).TrimStart(new[] { '.' });
                    }
                }

                MetricEventSource.Log.RaiseFunctionsInfoEvent(
                    siteName,
                    GetNormalizedString(function.Name),
                    function.Metadata != null
                        ? SerializeBindings(function.Metadata.InputBindings)
                        : GetNormalizedString(null),
                    function.Metadata != null
                        ? SerializeBindings(function.Metadata.OutputBindings) 
                        : GetNormalizedString(null),
                    GetNormalizedString(fileExtension),
                    function.Metadata != null ? function.Metadata.IsDisabled : false);
            }
        }

        private static string SerializeBindings(IEnumerable<BindingMetadata> bindings)
        {
            if (bindings != null)
            {
                return string.Join(",", bindings.ToList().Select(b => b.Type.ToString()));
            }
            else
            {
                return GetNormalizedString(null);
            }
        }

        private static string GetNormalizedString(string input)
        {
            return input ?? string.Empty;
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

            [Event(57908, Level = EventLevel.Informational, Channel = EventChannel.Operational)]
            public void RaiseFunctionsInfoEvent(string siteName, string functionName, string inputBindings, string outputBindings, string fileExtension, bool isDisabled)
            {
                if (IsEnabled())
                {
                    WriteEvent(57908, siteName, functionName, inputBindings, outputBindings, fileExtension, isDisabled);
                }
            }
        }
    }
}