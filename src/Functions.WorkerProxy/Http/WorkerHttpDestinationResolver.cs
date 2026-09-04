// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Azure.Functions.WorkerProxy.Http;

/// <summary>
/// Resolves the worker HTTP destination from configured and advertised endpoints.
/// </summary>
internal static class WorkerHttpDestinationResolver
{
    /// <summary>
    /// Resolves an absolute HTTP or HTTPS destination, preferring the configured override.
    /// </summary>
    /// <param name="overrideEndpoint">The configured destination override.</param>
    /// <param name="advertisedEndpoint">The worker-advertised destination.</param>
    /// <returns>The resolved destination, or <see langword="null"/> when neither endpoint is usable.</returns>
    public static Uri? Resolve(string? overrideEndpoint, string? advertisedEndpoint)
    {
        if (!string.IsNullOrWhiteSpace(overrideEndpoint))
        {
            return TryCreate(overrideEndpoint, out Uri? configuredDestination) ? configuredDestination : null;
        }

        return TryCreate(advertisedEndpoint, out Uri? advertisedDestination) ? advertisedDestination : null;
    }

    private static bool TryCreate(string? value, out Uri? destination)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out destination)
            || (!string.Equals(destination.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(destination.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            destination = null;
            return false;
        }

        return true;
    }
}
