// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host.Abstractions;
using Microsoft.Azure.WebJobs.Host.Executors;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry
{
    internal sealed class ActivitySourceWrapper : IActivitySourceAbstraction
    {
        private readonly ActivitySource _activitySource;

        public ActivitySourceWrapper(string sourceName, string version)
        {
            _activitySource = new ActivitySource(sourceName, version);
        }

        public IDisposable? StartActivity(IFunctionInstanceEx functionInstance)
        {
            if (Activity.Current != null)
            {
                return null;
            }

            // If no current activity exists, create one for the entire function run.
            // HTTP, Service Bus, Event Hub, and other instrumented triggers will have their own activities.
            // BeginFunctionScope creates a function activity when AppInsights SDK is enabled.
            // In OTel mode, Activity.Current will be null unless the trigger is instrumented.
            return _activitySource.StartActivity(functionInstance.FunctionDescriptor.LogName, ActivityKind.Server);
        }
    }
}
