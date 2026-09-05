// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;

namespace Azure.Functions.WorkerProxy;

/// <summary>
/// Enriches worker-facing ASP.NET Core activities with extension RPC correlation and concurrency data.
/// </summary>
internal static class ExtensionGrpcActivity
{
    private const string TagPrefix = "azure.functions.worker_proxy.extension_rpc";

    /// <summary>
    /// Enriches the current activity after an extension call acquires a host stream.
    /// </summary>
    /// <param name="callId">The logical extension call identifier.</param>
    /// <param name="streamId">The physical extension stream identifier.</param>
    /// <param name="activeCallCount">The active-call count when the call opens.</param>
    public static void CallOpened(string callId, string streamId, int activeCallCount)
    {
        Activity? activity = Activity.Current;
        activity?.SetTag($"{TagPrefix}.call_id", callId);
        activity?.SetTag($"{TagPrefix}.stream_id", streamId);
        activity?.SetTag($"{TagPrefix}.active_calls_at_open", activeCallCount);
    }

    /// <summary>
    /// Enriches the current activity with an extension call's final concurrency snapshot.
    /// </summary>
    /// <param name="activeCallCount">The active-call count when the call completes.</param>
    public static void CallCompleted(int activeCallCount)
    {
        Activity? activity = Activity.Current;
        activity?.SetTag($"{TagPrefix}.active_calls_at_completion", activeCallCount);
    }
}
