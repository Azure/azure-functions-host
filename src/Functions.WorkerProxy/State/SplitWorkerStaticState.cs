// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text.Json.Serialization;

namespace Azure.Functions.WorkerProxy.State;

internal sealed record SplitWorkerStaticState(
    [property: JsonRequired] SplitPodIdentity Self,
    [property: JsonRequired] bool IsDiscoveryPod,
    [property: JsonRequired] bool IsAlwaysReady,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? FunctionGroup,
    [property: JsonRequired] long ConfigLastModifiedTime,
    [property: JsonRequired] long ContentLastModifiedTime,
    [property: JsonRequired] DateTimeOffset StartupTime)
{
    public const string PodType = "FunctionsSplitWorkerPod";

    public static SplitWorkerStaticState CreatePlaceholder(string podName, DateTimeOffset startupTime) =>
        new(
            new(
                "unknown",
                podName,
                Guid.Empty,
                "unknown",
                PodType,
                new("unknown", 0, "unknown")),
            IsDiscoveryPod: false,
            IsAlwaysReady: false,
            FunctionGroup: null,
            ConfigLastModifiedTime: 0,
            ContentLastModifiedTime: 0,
            startupTime);
}
