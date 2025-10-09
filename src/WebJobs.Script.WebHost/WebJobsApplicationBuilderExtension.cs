// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Diagnostics.HealthChecks;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics.HealthChecks;
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
            IOptionsMonitor<StandbyOptions> standbyOptionsMonitor = builder.ApplicationServices.GetService<IOptionsMonitor<StandbyOptions>>();
            IOptionsMonitor<HttpBodyControlOptions> httpBodyControlOptionsMonitor = builder.ApplicationServices.GetService<IOptionsMonitor<HttpBodyControlOptions>>();
            IOptionsMonitor<ResponseCompressionOptions> responseCompressionOptionsMonitor = builder.ApplicationServices.GetService<IOptionsMonitor<ResponseCompressionOptions>>();

            IServiceProvider serviceProvider = builder.ApplicationServices;

            StandbyOptions standbyOptions = standbyOptionsMonitor.CurrentValue;
            standbyOptionsMonitor.OnChange(newOptions => standbyOptions = newOptions);

            HttpBodyControlOptions httpBodyControlOptions = httpBodyControlOptionsMonitor.CurrentValue;
            httpBodyControlOptionsMonitor.OnChange(newOptions => httpBodyControlOptions = newOptions);

            // Ensure the ClrOptimizationMiddleware is registered before all middleware
            builder.UseMiddleware<ClrOptimizationMiddleware>();
            builder.UseMiddleware<HttpRequestBodySizeMiddleware>();
            builder.UseMiddleware<SystemTraceMiddleware>();
            builder.UseMiddleware<HandleCancellationMiddleware>();
            builder.UseMiddleware<HostnameFixupMiddleware>();

            // Health is registered early in the pipeline to ensure it can avoid failures from the rest of the pipeline.
            builder.UseHealthChecks();

            if (environment.IsAnyLinuxConsumption() || environment.IsAnyKubernetesEnvironment())
            {
                builder.UseMiddleware<EnvironmentReadyCheckMiddleware>();
            }

#if PLACEHOLDER_SIMULATION
            if (standbyOptions.InStandbyMode)
            {
                builder.UseMiddleware<SpecializationSimulatorMiddleware>();
            }
#endif

            if (standbyOptions.InStandbyMode)
            {
                builder.UseMiddleware<PlaceholderSpecializationMiddleware>();
            }

            // Enable response compression only after PlaceholderSpecializationMiddleware, as it requires customer opt-in feature flag value.
            builder.UseWhen(_ => responseCompressionOptionsMonitor.CurrentValue.EnableResponseCompression, config =>
            {
                config.UseResponseCompression();
            });

            // Specialization can change the CompatMode setting, so this must run later than
            // the PlaceholderSpecializationMiddleware
            builder.UseWhen(context => httpBodyControlOptions.AllowSynchronousIO || context.Request.IsAdminDownloadRequest(), config =>
            {
                config.UseMiddleware<AllowSynchronousIOMiddleware>();
            });

            // This middleware must be registered before we establish the request service provider.
            builder.UseWhen(context => !context.Request.IsAdminRequest(), config =>
            {
                config.UseMiddleware<HostAvailabilityCheckMiddleware>();
            });

            builder.UseWhen(context => HostWarmupMiddleware.IsWarmUpRequest(context.Request, standbyOptions.InStandbyMode, environment), config =>
            {
                config.UseMiddleware<HostWarmupMiddleware>();
            });

            builder.UseWhen(context => !context.Request.IsAdminResumeRequest(), config =>
            {
                // This middleware must be registered before any other middleware depending on
                // JobHost/ScriptHost scoped services.
                config.UseMiddleware<ScriptHostRequestServiceProviderMiddleware>();
            });

            if (environment.IsLinuxAzureManagedHosting())
            {
                builder.UseMiddleware<AppServiceHeaderFixupMiddleware>();
            }

            builder.UseMiddleware<ExceptionMiddleware>();
            builder.UseWhen(HomepageMiddleware.IsHomepageRequest, config =>
            {
                config.UseMiddleware<HomepageMiddleware>();
            });
            builder.UseWhen(context => !context.Request.IsAdminRequest() && HttpThrottleMiddleware.ShouldEnable(serviceProvider), config =>
            {
                config.UseMiddleware<HttpThrottleMiddleware>();
            });

            builder.UseMiddleware<JobHostPipelineMiddleware>();
            builder.UseMiddleware<FunctionInvocationMiddleware>();

            // Register /admin/vfs, and /admin/zip to the VirtualFileSystem middleware.
            builder.UseWhen(VirtualFileSystemMiddleware.IsVirtualFileSystemRequest, config => config.UseMiddleware<VirtualFileSystemMiddleware>());

            // MVC routes (routes defined by Controllers like HostController, FunctionsController, ... must be added before functions/proxy routes so they are matched first and can not be overridden by functions or proxy routes)
            // source here: https://github.com/aspnet/Mvc/blob/master/src/Microsoft.AspNetCore.Mvc.Core/Builder/MvcApplicationBuilderExtensions.cs
            builder.UseMvc();

            // Ensure the HTTP binding routing is registered after all middleware
            builder.UseHttpBindingRouting(applicationLifetime, routes);

            return builder;
        }

        private static void UseHealthChecks(this IApplicationBuilder app)
        {
            // To start we are putting health under 'admin' to:
            // 1. Avoid conflicts with function routes.
            // 2. Allow for the same auth model as other admin APIs.
            // 3. Ensure this is always available to platform callers.
            // 4. Bypass easy-auth auth.
            const string healthPrefix = "/admin/health";
            static bool Predicate(HttpContext context)
            {
                return context.Request.Path.StartsWithSegments(healthPrefix);
            }

            app.MapWhen(Predicate, app =>
            {
                app.UseMiddleware<HealthCheckAuthMiddleware>();

                // This supports the ?wait={seconds} query string.
                app.UseMiddleware<HealthCheckWaitMiddleware>();

                app.UseHealthChecks(healthPrefix, new HealthCheckOptions
                {
                    ResponseWriter = HealthCheckResponseWriter.WriteResponseAsync,
                });

                app.UseHealthChecks($"{healthPrefix}/live", new HealthCheckOptions
                {
                    Predicate = r => r.Tags.Contains(HealthCheckTags.Liveness),
                    ResponseWriter = HealthCheckResponseWriter.WriteResponseAsync,
                });

                app.UseHealthChecks($"{healthPrefix}/ready", new HealthCheckOptions
                {
                    Predicate = r => r.Tags.Contains(HealthCheckTags.Readiness),
                    ResponseWriter = HealthCheckResponseWriter.WriteResponseAsync,
                });
            });
        }
    }
}
