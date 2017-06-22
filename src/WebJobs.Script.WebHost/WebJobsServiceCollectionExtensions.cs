// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace WebJobs.Script.WebHost.Core
{
    public static class WebJobsServiceCollectionExtensions
    {
        public static IServiceCollection AddWebJobsScriptHost(this IServiceCollection services)
        {
            // Add our script route handler
            services.TryAddSingleton<IWebJobsRouteHandler, ScriptRouteHandler>();

            services.AddHttpBindingRouting();

            return services;
        }
    }
}
