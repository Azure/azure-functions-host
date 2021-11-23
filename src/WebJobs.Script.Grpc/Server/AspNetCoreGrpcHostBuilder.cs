// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    internal static class AspNetCoreGrpcHostBuilder
    {
        public static readonly string SocketPath = Path.Combine(Path.GetTempPath(), "socket.tmp");

        public static IHostBuilder CreateHostBuilder(IScriptEventManager scriptEventManager, int port, ILogger logger) =>
            Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder.UseKestrel(options =>
                      {
                          if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                          {
                              if (File.Exists(SocketPath))
                              {
                                  File.Delete(SocketPath);
                              }
                              options.ListenUnixSocket(SocketPath, listenOptions =>
                              {
                                  listenOptions.Protocols = HttpProtocols.Http2;
                              });

                              Console.WriteLine($"Running LinuxUnixSocket on: {SocketPath}");
                          }
                          else
                          {
                              options.Listen(IPAddress.Parse(WorkerConstants.HostName), port, listenOptions =>
                              {
                                  listenOptions.Protocols = HttpProtocols.Http2;
                              });
                          }
                      });

                    webBuilder.ConfigureServices(services =>
                      {
                          services.AddSingleton(scriptEventManager);
                      });

                    webBuilder.UseStartup<Startup>();
                });
    }
}
