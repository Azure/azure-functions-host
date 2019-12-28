// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    public class GrpcServer : IRpcServer, IDisposable
    {
        private readonly string _ipAddress = "127.0.0.1";
        private bool _disposed = false;
        private IHostBuilder _grpcHostBuilder;
        private IHost _grpcHost;
        private int _port;

        public GrpcServer(IScriptEventManager scriptEventManager)
        {
            _port = WorkerHelpers.GetUnusedTcpPort();
            _grpcHostBuilder = GrpcHostBuilder.CreateHostBuilder(scriptEventManager, _ipAddress, _port);
        }

        public Uri Uri => new Uri($"http://{_ipAddress}:{_port}");

        public Task StartAsync()
        {
            _grpcHost = _grpcHostBuilder.Build();
            _grpcHost.Start();
            return Task.CompletedTask;
        }

        public Task ShutdownAsync() => _grpcHost.StopAsync();

        public Task KillAsync() => _grpcHost.StopAsync();

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _grpcHost.StopAsync().GetAwaiter().GetResult();
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}