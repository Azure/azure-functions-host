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
/// Exempts worker link requests from waiting for placeholder specialization.
/// </summary>
/// <remarks>
/// The script host starts only after the first worker links, and specialization waits for that start, so holding a
/// link request until specialization completes would deadlock. Instead, <see cref="WorkerLinkController"/> waits for
/// the specialized configuration through <see cref="WorkerLinkConfigurationMonitor"/>.
/// </remarks>
internal sealed class WorkerLinkSpecializationWaitExemption : ISpecializationWaitExemption
{
    private static readonly TemplateMatcher LinkWorkerRoute =
        new(TemplateParser.Parse(WorkerLinkController.LinkWorkerRoute), []);

    /// <summary>
    /// Matches <c>PUT</c> requests to the <see cref="WorkerLinkController.LinkWorker"/> route.
    /// </summary>
    /// <param name="request">The incoming request.</param>
    /// <returns><see langword="true"/> for a worker link request.</returns>
    public bool IsMatch(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return HttpMethods.IsPut(request.Method)
            && LinkWorkerRoute.TryMatch(request.Path, []);
    }
}
