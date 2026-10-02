// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;

namespace SampleIsolatedApp;

/// <summary>
/// Provides a deterministic HTTP response from the isolated worker.
/// </summary>
/// <param name="configuration">Configuration containing the optional harness worker identity.</param>
public sealed class Hello(IConfiguration configuration)
{
    /// <summary>
    /// Handles an anonymous HTTP invocation.
    /// </summary>
    /// <param name="request">The incoming Functions HTTP request.</param>
    /// <returns>The fixed plain-text response.</returns>
    [Function(nameof(Hello))]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "hello")] HttpRequest request)
    {
        if (configuration["ComputeSeparation:WorkerId"] is { } workerId)
        {
            request.HttpContext.Response.Headers["X-Compute-Worker-Id"] = workerId;
        }

        return new ContentResult
        {
            StatusCode = StatusCodes.Status200OK,
            ContentType = "text/plain",
            Content = "Hello from the BYOC .NET isolated worker."
        };
    }
}
