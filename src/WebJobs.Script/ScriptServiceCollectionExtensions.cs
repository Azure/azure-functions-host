// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class ScriptServiceCollectionExtensions
    {
        public static IServiceCollection AddManagedHostedService<T>(this IServiceCollection services) where T : class, IManagedHostedService
        {
            services.AddSingleton<T>();
            services.AddSingleton<IManagedHostedService, T>(d => d.GetService<T>());
            services.AddSingleton<IHostedService, T>(d => d.GetService<T>());

            return services;
        }
    }
}