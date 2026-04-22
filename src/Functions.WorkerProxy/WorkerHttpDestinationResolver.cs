// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.Functions.WorkerProxy;

/// <summary>
/// Resolves the destination URI the HTTP forwarding middleware should target for an
/// incoming worker request, applying the precedence contract:
///
/// <list type="number">
///   <item>The explicit <c>--worker-http-endpoint</c> override (CLI arg or environment
///   variable) wins when supplied. This exists for the Aspire dev harness, integration
///   tests, and any deployment where the operator wants to pin the destination
///   regardless of what the worker reports.</item>
///   <item>Otherwise, the dynamic <c>HttpUri</c> the worker advertised in its
///   <c>WorkerInitResponse</c> (or <c>FunctionEnvironmentReloadResponse</c>) capabilities
///   is used. Real .NET-isolated, Python, and Node v4 workers all bind to a
///   kernel-assigned loopback port at startup and advertise it this way.</item>
///   <item>If neither is available — or both are blank — the resolver returns
///   <see langword="null"/> and the middleware should respond with HTTP 503.</item>
/// </list>
///
/// Whitespace-only values in either source are treated as "not provided" so that an
/// empty environment variable does not silently shadow a valid worker-advertised URI.
/// Returned values are trimmed so leading/trailing whitespace from env vars (a
/// real-world hazard with Helm chart and docker-compose templating) does not feed
/// YARP a malformed URI and surface as 500s instead of the intended 503.
/// </summary>
// CS-TODO: This static resolver covers the only two destination sources we have today
// (explicit override and worker-advertised HttpUri). If/when a third source appears
// (e.g., per-request routing, sidecar discovery, multi-worker fan-out), refactor into
// a composable provider chain registered in DI so precedence is expressed by
// registration order rather than hard-coded if/else here. Defer until there is a third
// source — the interface shape (per-request HttpContext? startup-only string? both?)
// will fall out naturally from what that third source needs, and pre-committing to one
// here would lock in a guess.
internal static class WorkerHttpDestinationResolver
{
    /// <summary>
    /// Picks the destination URI according to the precedence rules described on the
    /// type. Returns <see langword="null"/> when no usable destination is available.
    /// </summary>
    /// <param name="overrideEndpoint">The value of <c>--worker-http-endpoint</c>, or
    /// <see langword="null"/> when no override was supplied.</param>
    /// <param name="advertisedEndpoint">The HttpUri the worker advertised via gRPC, or
    /// <see langword="null"/> when the worker has not (yet) reported one.</param>
    public static string? Resolve(string? overrideEndpoint, string? advertisedEndpoint)
    {
        if (!string.IsNullOrWhiteSpace(overrideEndpoint))
        {
            return overrideEndpoint.Trim();
        }

        if (!string.IsNullOrWhiteSpace(advertisedEndpoint))
        {
            return advertisedEndpoint.Trim();
        }

        return null;
    }
}
