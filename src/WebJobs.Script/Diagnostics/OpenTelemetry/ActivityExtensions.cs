// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry;

namespace Microsoft.Azure.WebJobs.Script.WebHost;

internal static class ActivityExtensions
{
    private static readonly ActivitySource _source = new(OpenTelemetryConstants.ActivitySourceNames.Host, OpenTelemetryConstants.HostActivitySourceVersion);

    /// <summary>
    /// Starts a specialization activity (kind: Internal).
    /// If the Activity.Current is null, the new activity becomes a root span.
    /// </summary>
    /// <returns>The started Activity, or null if the activity is not sampled.</returns>
    internal static Activity? StartSpecializationActivity()
    {
        return _source.StartActivity(OpenTelemetryConstants.SpecializationOperationName, ActivityKind.Internal);
    }

    /// <summary>
    /// Marks the activity as a cold start using the OpenTelemetry FaaS semantic convention.
    /// Safe to call even when the activity is null.
    /// </summary>
    /// <param name="activity">The activity to tag.</param>
    internal static void SetColdStartTag(this Activity? activity)
    {
        activity?.SetTag(ResourceSemanticConventions.FaaSColdStart, true);
    }

    /// <summary>
    /// Marks the activity as impacted by a cold start (custom Azure Functions tag).
    /// Safe to call even when the activity is null.
    /// </summary>
    /// <param name="activity">The activity to tag.</param>
    internal static void SetColdStartImpactedTag(this Activity? activity)
    {
        activity?.SetTag(ResourceSemanticConventions.FunctionsColdStartImpacted, true);
    }
}
