// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Controllers;
using Microsoft.Azure.WebJobs.Script.WebHost.Properties;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Filters
{
    /// <summary>
    /// Resource filter used to apply restrictions to requests made via the Functions ARM extension API (hostruntime).
    /// </summary>
    /// <remarks>
    /// All of our first class ARM APIs handle RBAC naturally. For the hostruntime bridge, the runtime collaborates
    /// based on request details coming from ARM/Geo.
    /// </remarks>
    public sealed class ArmExtensionResourceFilter : IAsyncResourceFilter
    {
        private static readonly char[] ExclusionDelimiters = ['|'];

        public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
        {
            // Here we want to identify ARM requests that were forwarded directly to us via the hostruntime bridge,
            // not hostruntime requests initiated internally by the geomaster. The latter requests won't have the
            // x-ms-arm-request-tracking-id header.
            var request = context.HttpContext.Request;
            bool isArmExtensionRequest = request.HasHeader(ScriptConstants.AntaresARMRequestTrackingIdHeader) &&
                request.HasHeader(ScriptConstants.AntaresARMExtensionsRouteHeader);

            if (isArmExtensionRequest)
            {
                if (await HandleArmExtensionRequestAsync(context))
                {
                    return;
                }
            }

            await next();
        }

        private static async Task<bool> HandleArmExtensionRequestAsync(ResourceExecutingContext context)
        {
            var request = context.HttpContext.Request;
            var controllerActionDescriptor = context.ActionDescriptor as ControllerActionDescriptor;
            if (IsExtensionWebHookAction(controllerActionDescriptor))
            {
                // Stamp the HttpContext so downstream consumers (e.g. the extension webhook
                // handler) can easily detect that the request was forwarded via the ARM
                // extensions bridge and apply ARM-specific behavior.
                context.HttpContext.Items[ScriptConstants.AzureFunctionsArmWebhookRequestKey] = true;

                string rawConfig = context.HttpContext.RequestServices
                    .GetService<IOptionsMonitor<FunctionsHostingConfigOptions>>()?.CurrentValue?.ArmWebhookOptInEnforcement;

                if (IsArmWebhookOptInEnforced(rawConfig, out IReadOnlySet<string> exclusions))
                {
                    string extensionName = context.RouteData.Values.TryGetValue("extensionName", out object value)
                        ? value as string
                        : null;
                    IScriptWebHookProvider provider = context.HttpContext.RequestServices.GetService<IScriptWebHookProvider>();

                    // Determine if target extension allows ARM requests.
                    if (!IsArmAllowedForExtension(extensionName, exclusions, provider))
                    {
                        context.HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.HttpContext.Response.WriteAsync(
                            string.Format(CultureInfo.InvariantCulture, Resources.ArmRequestNotAllowedForExtension, extensionName));
                        return true;
                    }
                }
            }

            if (string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                // requests made by owner/co-admin are not filtered
                if (!request.HasHeaderValue(ScriptConstants.AntaresClientAuthorizationSourceHeader, "legacy"))
                {
                    if (controllerActionDescriptor != null && controllerActionDescriptor.MethodInfo != null &&
                        Utility.GetHierarchicalAttributeOrNull<ResourceContainsSecretsAttribute>(controllerActionDescriptor.MethodInfo) != null)
                    {
                        // if the resource returned by the action contains secrets, fail the request
                        context.HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.HttpContext.Response.WriteAsync(Resources.UnauthorizedArmExtensionResourceRequest);
                        return true;
                    }
                }
            }

            return false;
        }

        internal static bool IsExtensionWebHookAction(ControllerActionDescriptor descriptor)
        {
            return descriptor?.MethodInfo is { } method &&
                method.DeclaringType == typeof(HostController) &&
                string.Equals(method.Name, nameof(HostController.ExtensionWebHookHandler), StringComparison.Ordinal);
        }

        // Determines whether ARM webhook opt-in enforcement is enabled, based on the raw
        // ArmWebhookOptInEnforcement hosting config value. Returns false when the value is null
        // or empty. When enabled, returns true and populates <paramref name="exclusions"/>
        // with the parsed '|'-delimited exemption set, or null when there are no exemptions.
        internal static bool IsArmWebhookOptInEnforced(string rawConfig, out IReadOnlySet<string> exclusions)
        {
            if (string.IsNullOrEmpty(rawConfig))
            {
                exclusions = null;
                return false;
            }

            string[] tokens = rawConfig.Split(ExclusionDelimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            exclusions = tokens.Length == 0
                ? null
                : new HashSet<string>(tokens, StringComparer.OrdinalIgnoreCase);

            return true;
        }

        internal static bool IsArmAllowedForExtension(string extensionName, IReadOnlySet<string> exclusions, IScriptWebHookProvider provider)
        {
            if (string.IsNullOrEmpty(extensionName))
            {
                return false;
            }

            if (exclusions is not null && exclusions.Contains(extensionName))
            {
                return true;
            }

            return provider is not null && provider.IsArmAllowed(extensionName);
        }
    }
}