// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Azure.Functions.WorkerProxy.Management;

internal sealed class SiteTokenAuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, SiteTokenValidator validator)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<SiteTokenAuthenticationMetadata>() is not null)
        {
            if (!context.Request.Headers.TryGetValue(SiteTokenValidator.HeaderName, out StringValues values)
                || values is not [string token]
                || !validator.IsValid(token))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
        }

        await next(context);
    }
}

internal sealed class SiteTokenAuthenticationMetadata
{
    public static SiteTokenAuthenticationMetadata Instance { get; } = new();
}
