// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    internal static class AspNetCoreGrpcHostBuilder
    {
        public static IHostBuilder CreateHostBuilder(
            FunctionRpc.FunctionRpcBase service,
            IScriptEventManager scriptEventManager,
            IScriptHostManager scriptHostManager,
            int port)
        {
            return new HostBuilder().ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseKestrel(options =>
                {
                    options.Listen(IPAddress.Parse(WorkerConstants.HostName), port, listenOptions =>
                    {
                        listenOptions.Protocols = HttpProtocols.Http2;
                    });
                });

                webBuilder.ConfigureServices(services =>
                {
                    services.AddSingleton(scriptHostManager);
                    services.AddSingleton(scriptEventManager);
                    services.AddSingleton(service);
                });

                webBuilder.UseStartup<Startup>();
            });
        }
    }
}