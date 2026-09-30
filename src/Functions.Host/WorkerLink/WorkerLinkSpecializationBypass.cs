// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;

namespace Azure.Functions.Host.WorkerLink;

/// <summary>
/// Lets worker link requests through placeholder specialization.
/// </summary>
/// <remarks>
/// The script host starts only after the first worker links, and specialization waits for that start, so holding a
/// link request until specialization completes would deadlock. <see cref="WorkerLinkSpecializationGate"/> holds the
/// link until the specialized configuration is in place.
/// </remarks>
internal sealed class WorkerLinkSpecializationBypass : IPlaceholderSpecializationBypass
{
    private static readonly PathString WorkersPath = new("/admin/workers");

    /// <summary>
    /// Matches <c>PUT /admin/workers/{workerPodName}</c>, the route of the worker link controller.
    /// </summary>
    /// <param name="request">The incoming request.</param>
    /// <returns><see langword="true"/> for a worker link request.</returns>
    public bool ShouldBypass(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!HttpMethods.IsPut(request.Method)
            || !request.Path.StartsWithSegments(WorkersPath, StringComparison.OrdinalIgnoreCase, out PathString remainder)
            || remainder.Value is not { Length: > 1 } value)
        {
            return false;
        }

        ReadOnlySpan<char> workerPodName = value.AsSpan(1);
        if (workerPodName[^1] == '/')
        {
            workerPodName = workerPodName[..^1];
        }

        return !workerPodName.IsEmpty && !workerPodName.Contains('/');
    }
}
