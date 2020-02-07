// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
            IEnvironment environment = builder.ApplicationServices.GetService<IEnvironment>() ?? SystemEnvironment.Instance;
            IOptionsMonitor<StandbyOptions> standbyOptions = builder.ApplicationServices.GetService<IOptionsMonitor<StandbyOptions>>();
            IOptionsMonitor<HttpBodyControlOptions> httpBodyControlOptions = builder.ApplicationServices.GetService<IOptionsMonitor<HttpBodyControlOptions>>();
            IOptionsMonitor<HttpOptions> httpOptions = builder.ApplicationServices.GetService<IOptionsMonitor<HttpOptions>>();

            builder.UseMiddleware<SystemTraceMiddleware>();
            builder.UseMiddleware<HostnameFixupMiddleware>();
            if (environment.IsLinuxConsumption())
            {
                builder.UseMiddleware<EnvironmentReadyCheckMiddleware>();
            }

            if (standbyOptions.CurrentValue.InStandbyMode)
            {
                builder.UseMiddleware<PlaceholderSpecializationMiddleware>();
            }

            // Specialization can change the CompatMode setting, so this must run later than
            // the PlaceholderSpecializationMiddleware
            builder.UseWhen(context => httpBodyControlOptions.CurrentValue.AllowSynchronousIO, config =>
            {
                config.UseMiddleware<AllowSynchronousIOMiddleware>();
            });

            // This middleware must be registered before we establish the request service provider.
            builder.UseWhen(context => !context.Request.IsAdminRequest(), config =>
            {
                config.UseMiddleware<HostAvailabilityCheckMiddleware>();
            });

            builder.UseWhen(context => HostWarmupMiddleware.IsWarmUpRequest(context.Request, standbyOptions.CurrentValue.InStandbyMode, environment), config =>
            {
                builder.UseMiddleware<HostWarmupMiddleware>();
            });

            // This middleware must be registered before any other middleware depending on
            // JobHost/ScriptHost scoped services.
            builder.UseMiddleware<ScriptHostRequestServiceProviderMiddleware>();

            if (environment.IsLinuxConsumption())
            {
                builder.UseMiddleware<AppServiceHeaderFixupMiddleware>();
            }

            builder.UseMiddleware<ExceptionMiddleware>();
            builder.UseWhen(HomepageMiddleware.IsHomepageRequest, config =>
            {
                config.UseMiddleware<HomepageMiddleware>();
            });
            builder.UseWhen(context => HttpThrottleMiddleware.ShouldEnable(httpOptions.CurrentValue) && !context.Request.IsAdminRequest(), config =>
            {
                config.UseMiddleware<HttpThrottleMiddleware>();
            });
            builder.UseMiddleware<ResponseContextItemsCheckMiddleware>();
            builder.UseMiddleware<JobHostPipelineMiddleware>();
            builder.UseMiddleware<FunctionInvocationMiddleware>();

            // Register /admin/vfs, and /admin/zip to the VirtualFileSystem middleware.
            builder.UseWhen(VirtualFileSystemMiddleware.IsVirtualFileSystemRequest, config => config.UseMiddleware<VirtualFileSystemMiddleware>());

            // Ensure the HTTP binding routing is registered after all middleware
            builder.UseHttpBindingRouting(applicationLifetime, routes);

            builder.UseMvc();

            return builder;
        }
    }
}