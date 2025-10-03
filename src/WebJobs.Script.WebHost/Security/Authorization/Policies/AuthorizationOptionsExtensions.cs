// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies
{
    public static class AuthorizationOptionsExtensions
    {
        public static void AddScriptPolicies(this AuthorizationOptions options)
        {
            options.AddPolicy(PolicyNames.AdminAuthLevel, p =>
            {
                p.AddScriptAuthenticationSchemes();
                p.AddRequirements(new AuthLevelRequirement(AuthorizationLevel.Admin));
                p.RequireAssertion(c =>
                {
                    if (c.Resource is AuthorizationFilterContext filterContext)
                    {
                        if (!EnforceAdminIsolation(filterContext.HttpContext, allowAppServiceInternal: true))
                        {
                            return false;
                        }
                    }

                    return true;
                });
            });

            options.AddPolicy(PolicyNames.ExtensionWebhookInvoke, p =>
            {
                p.AddScriptAuthenticationSchemes();
                p.RequireAssertion(c =>
                {
                    if (c.Resource is AuthorizationFilterContext filterContext)
                    {
                        string extensionName = filterContext.RouteData.Values["extensionName"]?.ToString();

                        // First check to see if anonymous access has been configured for the webhook.
                        // E.g. this might be configured for a Webhook extension in an app where App Service
                        // authentication is being used.
                        var snapshot = filterContext.HttpContext.RequestServices.GetRequiredService<IOptionsSnapshot<ExtensionSystemOptions>>();
                        var extensionSystemOptions = snapshot.Get(extensionName);
                        if (!string.IsNullOrEmpty(extensionName) && extensionSystemOptions?.WebhookAuthorizationLevel == AuthorizationLevel.Anonymous)
                        {
                            return true;
                        }

                        string keyName = !string.IsNullOrEmpty(extensionName)
                            ? DefaultScriptWebHookProvider.GetKeyName(extensionName)
                            : filterContext.RouteData.Values["keyName"]?.ToString();

                        if (!string.IsNullOrEmpty(keyName) && AuthUtility.PrincipalHasAuthLevelClaim(filterContext.HttpContext.User, AuthorizationLevel.System, keyName))
                        {
                            return true;
                        }
                    }

                    return false;
                });
            });
        }

        private static void AddScriptAuthenticationSchemes(this AuthorizationPolicyBuilder builder)
        {
            builder.AuthenticationSchemes.Add(AuthLevelAuthenticationDefaults.AuthenticationScheme);
            builder.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        }

        /// <summary>
        /// When AdminIsolation is enabled, performs platform internal request verification on the specified HTTP context.
        /// </summary>
        /// <param name="httpContext">The <see cref="HttpContext"/> to check.</param>
        /// <param name="allowAppServiceInternal">True if App Service internal requests should also be allowed; otherwise, false.</param>
        /// <returns>True if the request passes isolation checks, false otherwise.</returns>
        internal static bool EnforceAdminIsolation(HttpContext httpContext, bool allowAppServiceInternal)
        {
            var environment = httpContext.RequestServices.GetRequiredService<IEnvironment>();
            if (environment.IsAdminIsolationEnabled() &&
                !(httpContext.Request.IsPlatformInternalRequest(environment) || (allowAppServiceInternal && httpContext.Request.IsAppServiceInternalRequest())))
            {
                // request must either be granted PlatformInternal by FrontEnd, or must be an internal
                // request that has bypassed FrontEnd
                return false;
            }

            return true;
        }
    }
}
