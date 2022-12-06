// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Extensions.DependencyInjection;

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
                        if (!CheckPlatformInternal(filterContext.HttpContext, allowAppServiceInternal: false))
                        {
                            return false;
                        }
                    }

                    return true;
                });
            });

            options.AddPolicy(PolicyNames.SystemAuthLevel, p =>
            {
                p.AddScriptAuthenticationSchemes();
                p.AddRequirements(new AuthLevelRequirement(AuthorizationLevel.System));
            });

            options.AddPolicy(PolicyNames.AdminAuthLevelOrInternal, p =>
            {
                p.AddScriptAuthenticationSchemes();
                p.RequireAssertion(async c =>
                {
                    if (c.Resource is AuthorizationFilterContext filterContext)
                    {
                        if (!CheckPlatformInternal(filterContext.HttpContext, allowAppServiceInternal: true))
                        {
                            return false;
                        }

                        if (filterContext.HttpContext.Request.IsAppServiceInternalRequest())
                        {
                            return true;
                        }

                        var authorizationService = filterContext.HttpContext.RequestServices.GetRequiredService<IAuthorizationService>();
                        AuthorizationResult result = await authorizationService.AuthorizeAsync(c.User, PolicyNames.AdminAuthLevel);

                        return result.Succeeded;
                    }

                    return false;
                });
            });

            options.AddPolicy(PolicyNames.SystemKeyAuthLevel, p =>
            {
                p.AddScriptAuthenticationSchemes();
                p.RequireAssertion(c =>
                {
                    if (c.Resource is AuthorizationFilterContext filterContext)
                    {
                        if (filterContext.HttpContext.Request.IsAppServiceInternalRequest())
                        {
                            return true;
                        }

                        string keyName = null;
                        object keyNameObject = filterContext.RouteData.Values["extensionName"];
                        if (keyNameObject != null)
                        {
                            keyName = DefaultScriptWebHookProvider.GetKeyName(keyNameObject.ToString());
                        }
                        else
                        {
                            keyNameObject = filterContext.RouteData.Values["keyName"];
                            if (keyNameObject != null)
                            {
                                keyName = keyNameObject.ToString();
                            }
                        }

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
            builder.AuthenticationSchemes.Add(ArmAuthenticationDefaults.AuthenticationScheme);
            builder.AuthenticationSchemes.Add(AuthLevelAuthenticationDefaults.AuthenticationScheme);
            builder.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        }

        internal static bool CheckPlatformInternal(HttpContext httpContext, bool allowAppServiceInternal)
        {
            // when AdminIsolation is enabled, verify the request is platform internal
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
