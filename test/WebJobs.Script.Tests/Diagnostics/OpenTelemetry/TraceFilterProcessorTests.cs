// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics.OpenTelemetry
{
    public class TraceFilterProcessorTests
    {
        [Fact]
        public void OnEnd_DropsDependencyTraces()
        {
            var activityListener = new ActivityListener();
            activityListener.ShouldListenTo = activitySource => activitySource.Name == "Azure.Core.Http" || activitySource.Name == "AnotherActivitySource";

            activityListener.ActivityStarted = ActivityStarted;
            activityListener.ActivityStopped = ActivityStopped;
            activityListener.Sample = Sample;

            ActivitySource.AddActivityListener(activityListener);

            void ActivityStarted(Activity activity)
            {
            }

            void ActivityStopped(Activity activity)
            {
            }

            ActivitySamplingResult Sample(ref ActivityCreationOptions<ActivityContext> context)
            {
                return ActivitySamplingResult.AllData;
            }
            ActivitySource sampleActivitySource = new ActivitySource("Azure.Core.Http");
            ActivitySource anotherActivitySource = new ActivitySource("AnotherActivitySource");

            using (var activity = sampleActivitySource.StartActivity("Test"))
            {
                activity.AddTag("url.full", "https://applicationinsights.azure.com/some/path");
                activity.ActivityTraceFlags = ActivityTraceFlags.Recorded;
                var processor = TraceFilterProcessor.Instance;

                // Act
                processor.OnEnd(activity);

                // Assert
                Assert.Equal(ActivityTraceFlags.None, activity.ActivityTraceFlags);
            }

            using (var activity = anotherActivitySource.StartActivity("Test"))
            {
                activity.AddTag("url.full", "https://applicationinsights.azure.com/some/path");
                activity.ActivityTraceFlags = ActivityTraceFlags.Recorded;
                var processor = TraceFilterProcessor.Instance;

                // Act
                processor.OnEnd(activity);

                // Assert
                Assert.Equal(ActivityTraceFlags.Recorded, activity.ActivityTraceFlags);
            }

            using (var activity = sampleActivitySource.StartActivity("Test"))
            {
                activity.AddTag("url.full", "/AzureFunctionsRpcMessages.FunctionRpc/");
                activity.ActivityTraceFlags = ActivityTraceFlags.Recorded;
                var processor = TraceFilterProcessor.Instance;

                // Act
                processor.OnEnd(activity);

                // Assert
                Assert.Equal(ActivityTraceFlags.None, activity.ActivityTraceFlags);
            }

            using (var activity = sampleActivitySource.StartActivity("Test"))
            {
                activity.AddTag("az.namespace", "Microsoft.Storage");
                activity.AddTag("url.full", "/azure-webjobs-");
                activity.ActivityTraceFlags = ActivityTraceFlags.Recorded;
                var processor = TraceFilterProcessor.Instance;

                // Act
                processor.OnEnd(activity);

                // Assert
                Assert.Equal(ActivityTraceFlags.None, activity.ActivityTraceFlags);
            }
        }
    }
}
