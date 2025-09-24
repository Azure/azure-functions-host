// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics.HealthChecks
{
    public sealed class HealthCheckAuthMiddleware(
        RequestDelegate next, IPolicyEvaluator policy, IAuthorizationPolicyProvider provider)
    {
        private readonly RequestDelegate _next = next ?? throw new ArgumentNullException(nameof(next));
        private readonly IPolicyEvaluator _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        private readonly IAuthorizationPolicyProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));

        public async Task InvokeAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            AuthorizationPolicy policy = await _provider.GetPolicyAsync(PolicyNames.AdminAuthLevel)
                .ConfigureAwait(false);

            AuthenticateResult authentication = await _policy.AuthenticateAsync(policy, context)
                .ConfigureAwait(false);

            if (!authentication.Succeeded)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            PolicyAuthorizationResult authorization = await _policy.AuthorizeAsync(
                policy, authentication, context, null).ConfigureAwait(false);

            if (!authorization.Succeeded)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await _next(context);
        }
    }
}
