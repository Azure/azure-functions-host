// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Buffering;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public static class WebJobsApplicationBuilderExtension
    {
        public static IApplicationBuilder UseWebJobsScriptHost(this IApplicationBuilder builder, IApplicationLifetime applicationLifetime)
        {
            return UseWebJobsScriptHost(builder, applicationLifetime, null);
        }

        public static IApplicationBuilder UseWebJobsScriptHost(this IApplicationBuilder builder, IApplicationLifetime applicationLifetime, Action<WebJobsRouteBuilder> routes)
        {
            // This middleware must be registered before we establish the request service provider.
            builder.UseWhen(context => !context.Request.Path.StartsWithSegments("/admin"), config =>
            {
                config.UseMiddleware<HostAvailabilityCheckMiddleware>();
            });

            // This middleware must be registered before any other middleware depending on
            // JobHost/ScriptHost scoped services.
            builder.UseMiddleware<ScriptHostRequestServiceProviderMiddleware>();

            if (!ScriptSettingsManager.Instance.IsAppServiceEnvironment)
            {
                builder.UseMiddleware<AppServiceHeaderFixupMiddleware>();
            }

            builder.UseMiddleware<EnvironmentReadyCheckMiddleware>();
            builder.UseMiddleware<HttpExceptionMiddleware>();
            builder.UseMiddleware<ResponseBufferingMiddleware>();
            builder.UseMiddleware<HomepageMiddleware>();
            builder.UseMiddleware<HttpThrottleMiddleware>();
            builder.UseMiddleware<FunctionInvocationMiddleware>();
            builder.UseMiddleware<HostWarmupMiddleware>();

            // Register /admin/vfs, and /admin/zip to the VirtualFileSystem middleware.
            builder.UseWhen(VirtualFileSystemMiddleware.IsVirtualFileSystemRequest, config => config.UseMiddleware<VirtualFileSystemMiddleware>());

            // Ensure the HTTP binding routing is registered after all middleware
            builder.UseHttpBindingRouting(applicationLifetime, routes);

            builder.UseMvc();

            return builder;
        }
    }
}