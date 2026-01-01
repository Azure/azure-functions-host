// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.WebHost;

internal static class ActivityExtensions
{
    private static readonly ActivitySource _source = new("Microsoft.Azure.Functions.Host");

    /// <summary>
    /// Starts a specialization activity (kind: Internal) using the parent Activity's context.
    /// If the parent activity is null, the new activity becomes a root span.
    /// </summary>
    /// <param name="parentActivity">The parent Activity whose context should be used.</param>
    /// <returns>The started Activity, or null if the activity is not sampled.</returns>
    internal static Activity? StartSpecializationActivity(this Activity? parentActivity)
    {
        var parentContext = parentActivity?.Context ?? default;

        return _source.StartActivity("init", ActivityKind.Internal, parentContext);
    }

    /// <summary>
    /// Marks the activity as a cold start using the OpenTelemetry FaaS semantic convention.
    /// Safe to call even when the activity is null.
    /// </summary>
    /// <param name="activity">The activity to tag.</param>
    internal static void SetColdStartTag(this Activity? activity)
    {
        activity?.SetTag("faas.coldstart", true);
    }

    /// <summary>
    /// Marks the activity as impacted by a cold start (custom Azure Functions tag).
    /// Safe to call even when the activity is null.
    /// </summary>
    /// <param name="activity">The activity to tag.</param>
    internal static void SetColdStartImpactedTag(this Activity? activity)
    {
        activity?.SetTag("azure.functions.coldstart_impacted", true);
    }
}
