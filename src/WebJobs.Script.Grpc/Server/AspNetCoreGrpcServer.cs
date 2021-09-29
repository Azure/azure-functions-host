// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Workers;
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
            int port = WorkerUtilities.GetUnusedTcpPort();
            _grpcHostBuilder = AspNetCoreGrpcHostBuilder.CreateHostBuilder(scriptEventManager, port);
            _logger = logger;
            Uri = new Uri($"http://{WorkerConstants.HostName}:{port}");
        }

        public Uri Uri { get; private set; }

        public async Task StartAsync()
        {
            _grpcHost = _grpcHostBuilder.Build();

            await _grpcHost.StartAsync();

            // Get the actual address we've started on.
            var server = _grpcHost.Services.GetService<IServer>();
            var addressFeature = server?.Features.Get<IServerAddressesFeature>();
            var address = addressFeature?.Addresses.SingleOrDefault();

            if (!Uri.TryCreate(address, UriKind.Absolute, out Uri uri) ||
                Uri != uri)
            {
                _logger.LogWarning($"Configured Uri ({Uri}) does not match actual Uri ({uri}).");
            }

            _logger.LogDebug($"Started {nameof(AspNetCoreGrpcServer)} on {address}.");
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
