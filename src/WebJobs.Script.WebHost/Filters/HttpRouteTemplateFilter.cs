// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Features;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Filters;

/// <summary>
/// Captures the selected MVC route before authorization can short-circuit the request.
/// </summary>
internal sealed class HttpRouteTemplateFilter : IAuthorizationFilter, IOrderedFilter
{
    /// <inheritdoc/>
    public int Order => int.MinValue;

    /// <inheritdoc/>
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.ActionDescriptor.AttributeRouteInfo?.Template is string template)
        {
            context.HttpContext.Features.Set(new HttpRouteTemplateFeature(template));
        }
    }
}
