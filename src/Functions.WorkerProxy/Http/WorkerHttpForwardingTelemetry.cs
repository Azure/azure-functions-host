// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using Yarp.ReverseProxy.Forwarder;

namespace Azure.Functions.WorkerProxy.Http;

/// <summary>
/// Enriches the inbound HTTP request activity with worker forwarding outcomes.
/// </summary>
internal static class WorkerHttpForwardingTelemetry
{
    internal const string ForwardingResultAttribute = "azure.functions.worker_proxy.http.forwarding.result";
    internal const string ForwarderErrorAttribute = "azure.functions.worker_proxy.http.forwarding.error";

    internal const string DestinationNotConfiguredResult = "DestinationNotConfigured";
    internal const string DestinationNotReadyResult = "DestinationNotReady";
    internal const string ForwarderErrorResult = "ForwarderError";

    /// <summary>
    /// Records that no worker HTTP destination was configured or advertised.
    /// </summary>
    public static void RecordDestinationNotConfigured()
    {
        RecordFailure(DestinationNotConfiguredResult);
    }

    /// <summary>
    /// Records that the worker HTTP destination did not become ready.
    /// </summary>
    public static void RecordDestinationNotReady(WorkerEndpointReadinessResult readinessResult)
    {
        RecordFailure(DestinationNotReadyResult, readinessResult.ToString());
    }

    /// <summary>
    /// Records an error returned by the YARP HTTP forwarder.
    /// </summary>
    public static void RecordForwarderError(ForwarderError error)
    {
        RecordFailure(ForwarderErrorResult, error.ToString());
    }

    private static void RecordFailure(string result, string? error = null)
    {
        Activity? activity = Activity.Current;
        activity?.SetTag(ForwardingResultAttribute, result);

        if (error is not null)
        {
            activity?.SetTag(ForwarderErrorAttribute, error);
        }

        activity?.SetStatus(ActivityStatusCode.Error);
    }
}
