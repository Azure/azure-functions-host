// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Yarp.ReverseProxy.Forwarder;

namespace Azure.Functions.WorkerProxy.Http;

/// <summary>
/// Enriches the inbound HTTP request activity with worker forwarding outcomes.
/// </summary>
internal static class WorkerHttpForwardingTelemetry
{
    internal const string ForwardingResultAttribute = "azure.functions.worker_proxy.http.forwarding.result";
    internal const string ForwarderErrorAttribute = "azure.functions.worker_proxy.http.forwarding.error";
    internal const string ErrorTypeAttribute = "error.type";

    internal const string CanceledResult = "canceled";
    internal const string DestinationNotConfiguredResult = "destination_not_configured";
    internal const string DestinationNotReadyResult = "destination_not_ready";
    internal const string ForwarderErrorResult = "forwarder_error";

    internal const string ForwarderErrorTypePrefix = "Yarp.ReverseProxy.Forwarder.ForwarderError.";

    /// <summary>
    /// Records that forwarding was canceled by the caller.
    /// </summary>
    public static void RecordCanceled(HttpContext context)
    {
        SetResult(context, CanceledResult);
    }

    /// <summary>
    /// Records that no worker HTTP destination was configured or advertised.
    /// </summary>
    public static void RecordDestinationNotConfigured(HttpContext context)
    {
        SetError(context, DestinationNotConfiguredResult);
    }

    /// <summary>
    /// Records that the worker HTTP destination did not become ready.
    /// </summary>
    public static void RecordDestinationNotReady(HttpContext context)
    {
        SetError(context, DestinationNotReadyResult);
    }

    /// <summary>
    /// Records an error returned by the YARP HTTP forwarder.
    /// </summary>
    public static void RecordForwarderError(HttpContext context, ForwarderError error)
    {
        SetError(context, ForwarderErrorResult, error);
    }

    private static void SetResult(HttpContext context, string result)
    {
        GetRequestActivity(context)?.SetTag(ForwardingResultAttribute, result);
    }

    private static void SetError(HttpContext context, string result, ForwarderError? forwarderError = null)
    {
        Activity? activity = GetRequestActivity(context);
        activity?.SetTag(ForwardingResultAttribute, result);

        if (forwarderError is not null)
        {
            activity?.SetTag(ForwarderErrorAttribute, forwarderError.Value.ToString());
        }

        if (activity?.GetTagItem(ErrorTypeAttribute) is null)
        {
            string errorType = context.Response.StatusCode >= StatusCodes.Status500InternalServerError
                ? context.Response.StatusCode.ToString(CultureInfo.InvariantCulture)
                : $"{ForwarderErrorTypePrefix}{forwarderError}";
            activity?.SetTag(ErrorTypeAttribute, errorType);
        }

        activity?.SetStatus(ActivityStatusCode.Error);
    }

    private static Activity? GetRequestActivity(HttpContext context)
    {
        return context.Features.Get<IHttpActivityFeature>()?.Activity;
    }
}
