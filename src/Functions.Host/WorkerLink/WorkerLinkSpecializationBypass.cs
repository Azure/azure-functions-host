// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Azure.Functions.Host.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
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
    private static readonly TemplateMatcher WorkerLinkRoute =
        new(TemplateParser.Parse(WorkerLinkController.Route), new RouteValueDictionary());

    /// <summary>
    /// Matches <c>PUT</c> requests to the <see cref="WorkerLinkController"/> route.
    /// </summary>
    /// <param name="request">The incoming request.</param>
    /// <returns><see langword="true"/> for a worker link request.</returns>
    public bool ShouldBypass(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return HttpMethods.IsPut(request.Method)
            && WorkerLinkRoute.TryMatch(request.Path, new RouteValueDictionary());
    }
}