﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;

namespace WebJobs.Script.WebHost.Core
{
    public static class WebJobsApplicationBuilderExtension
    {
        public static IApplicationBuilder UseWebJobsScriptHost(this IApplicationBuilder builder, IApplicationLifetime applicationLifetime)
        {
            return UseWebJobsScriptHost(builder, applicationLifetime, null);
        }

        public static IApplicationBuilder UseWebJobsScriptHost(this IApplicationBuilder builder, IApplicationLifetime applicationLifetime, Action<WebJobsRouteBuilder> routes)
        {
            WebScriptHostManager hostManager = builder.ApplicationServices.GetService(typeof(WebScriptHostManager)) as WebScriptHostManager;

            builder.UseHttpBindingRouting(applicationLifetime, routes);

            builder.UseWhen(context => !context.Request.Path.StartsWithSegments("/admin/host/status"), config =>
            {
                config.UseMiddleware<ScriptHostCheckMiddleware>();
            });

            builder.UseMvc(r =>
            {
                r.MapRoute(name: "Home",
                    template: string.Empty,
                    defaults: new { controller = "Home", action = "Get" });
            });

            return builder;
        }
    }
}
