// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Rpc.Hosting
{
    public static class RpcServiceCollectionExtensions
    {
        public static IServiceCollection AddRpcScriptHostServices(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            // HTTP Worker
            services.AddSingleton<IHttpWorkerProcessFactory, HttpWorkerProcessFactory>();
            services.AddSingleton<IHttpWorkerChannelFactory, HttpWorkerChannelFactory>();
            services.AddSingleton<IHttpWorkerService, DefaultHttpWorkerService>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IFunctionProvider, HttpWorkerFunctionProvider>());

            // Rpc Worker
            services.AddSingleton<IJobHostRpcWorkerChannelManager, JobHostRpcWorkerChannelManager>();
            services.AddSingleton<IRpcFunctionInvocationDispatcherLoadBalancer, RpcFunctionInvocationDispatcherLoadBalancer>();

            //Worker Function Invocation dispatcher
            services.AddSingleton<IFunctionInvocationDispatcherFactory, FunctionInvocationDispatcherFactory>();

            services.AddSingleton<IHostedService, WorkerConcurrencyManager>();
                
            // Configuration
            services.AddSingleton<IPostConfigureOptions<ScriptHostRecycleOptions>, HttpScriptHostRecycleOptionsSetup>();
            services.ConfigureOptions<HttpWorkerOptionsSetup>();

            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, FunctionInvocationDispatcherShutdownManager>());
            services.AddManagedHostedService<RpcInitializationService>();

            // Add Language Worker Service
            services.AddSingleton<IRpcWorkerProcessFactory, RpcWorkerProcessFactory>();
            services.TryAddSingleton<IWebHostRpcWorkerChannelManager, WebHostRpcWorkerChannelManager>();

            return services;
        }
    }
}
