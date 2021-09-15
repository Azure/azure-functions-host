// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    public class AspNetCoreGrpcServer : IRpcServer, IDisposable, IAsyncDisposable
    {
        private readonly IHostBuilder _grpcHostBuilder;
        private readonly ILogger<AspNetCoreGrpcServer> _logger;
        private bool _disposed = false;
        private IHost _grpcHost;

        public AspNetCoreGrpcServer(IScriptEventManager scriptEventManager, ILogger<AspNetCoreGrpcServer> logger)
        {
            _grpcHostBuilder = AspNetCoreGrpcHostBuilder.CreateHostBuilder(scriptEventManager);
            _logger = logger;
        }

        public Uri Uri { get; private set; }

        public Task StartAsync()
        {
            _grpcHost = _grpcHostBuilder.Build();

            _grpcHost.Start();

            var server = _grpcHost.Services.GetService<IServer>();
            var addressFeature = server.Features.Get<IServerAddressesFeature>();
            var address = addressFeature.Addresses.Single();

            Uri.TryCreate(address, UriKind.Absolute, out Uri uri);
            Uri = uri;

            _logger.LogDebug($"Started {nameof(AspNetCoreGrpcServer)} on {address}.");

            return Task.CompletedTask;
        }

        public Task ShutdownAsync() => _grpcHost.StopAsync();

        public Task KillAsync() => _grpcHost.StopAsync();

        protected async ValueTask DisposeAsync(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    await _grpcHost.StopAsync();
                    _grpcHost.Dispose();
                }
                _disposed = true;
            }
        }

        public ValueTask DisposeAsync()
        {
            return DisposeAsync(true);
        }

        public void Dispose()
        {
            DisposeAsync();
        }
    }
}
