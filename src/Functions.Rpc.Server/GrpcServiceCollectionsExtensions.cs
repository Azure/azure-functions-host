// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    /// <summary>
    /// Extension methods for registering the inbound FunctionRpc server.
    /// </summary>
    public static class GrpcServiceCollectionsExtensions
    {
        /// <summary>
        /// Adds the inbound FunctionRpc server and worker channel services.
        /// </summary>
        /// <param name="services">The service collection to update.</param>
        /// <returns>The updated service collection.</returns>
        public static IServiceCollection AddScriptGrpc(this IServiceCollection services)
        {
            services.AddSingleton<IRpcWorkerChannelFactory, GrpcWorkerChannelFactory>();
            services.AddSingleton<FunctionRpc.FunctionRpcBase, FunctionRpcService>();

            services.AddSingleton<IRpcServer, AspNetCoreGrpcServer>();

            services.AddHttpForwarder();
            services.AddSingleton<IHttpProxyService, DefaultHttpProxyService>();

            return services;
        }
    }
}