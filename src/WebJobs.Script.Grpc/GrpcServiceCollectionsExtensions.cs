// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    public static class GrpcServiceCollectionsExtensions
    {
        public static IServiceCollection AddScriptGrpc(this IServiceCollection services)
        {
            services.AddManagedHostedService<RpcInitializationService>();
            services.AddSingleton<IRpcWorkerProcessFactory, RpcWorkerProcessFactory>();
            services.AddSingleton<FunctionRpc.FunctionRpcBase, FunctionRpcService>();
            services.AddSingleton<IRpcServer, GrpcServer>();
            services.AddSingleton<IRpcWorkerChannelFactory, RpcWorkerChannelFactory>();

            services.AddGrpc();

            return services;
        }
    }
}
