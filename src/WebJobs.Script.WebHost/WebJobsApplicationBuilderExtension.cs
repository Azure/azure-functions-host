// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Extensions.Http;
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
            builder.UseMiddleware<FunctionInvocationMiddleware>();
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
