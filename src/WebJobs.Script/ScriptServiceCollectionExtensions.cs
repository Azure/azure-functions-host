// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class ScriptServiceCollectionExtensions
    {
        /// <summary>
        /// Allows serving listeners for all function groups while retaining validation-runtime suppression.
        /// </summary>
        /// <param name="services">The ScriptHost service collection.</param>
        /// <returns>The supplied service collection.</returns>
        public static IServiceCollection UseGroupNeutralFunctionListeners(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.Configure<FunctionGroupListenerOptions>(options => options.RestrictListenersToTargetGroup = false);

            return services;
        }

        public static IServiceCollection AddManagedHostedService<T>(this IServiceCollection services) where T : class, IManagedHostedService
        {
            services.AddSingleton<T>();
            services.AddSingleton<IManagedHostedService, T>(d => d.GetService<T>());
            services.AddSingleton<IHostedService, T>(d => d.GetService<T>());

            return services;
        }
    }
}