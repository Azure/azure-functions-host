// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using Microsoft.Extensions.Logging;
using WorkerHarness.Core.Commons;

namespace WorkerHarness.Core.GrpcService
{
    public sealed class GrpcServer : IGrpcServer
    {
        private readonly ILogger<GrpcServer> _logger;
        private readonly Grpc.Core.Server _grpcServer;

        public GrpcServer(GrpcServiceChannel channel, ILogger<GrpcServer> logger)
        {
            _logger = logger;

            int port = WorkerUtilities.GetUnusedTcpPort();
            Uri = new Uri($"http://{HostConstants.HostName}:{port}");

            GrpcService grpcService = new(channel.InboundChannel, channel.OutboundChannel);
            _grpcServer = new()
            {
                Services = { FunctionRpc.BindService(grpcService) },
                Ports = { new Grpc.Core.ServerPort(Uri.Host, Uri.Port, Grpc.Core.ServerCredentials.Insecure) }
            };
        }
        public Uri Uri { get; }

        public async Task Shutdown()
        {
            _logger.LogInformation("Shutting down gRPC server");
            await _grpcServer.ShutdownAsync();
        }

        public void Start()
        {
            _logger.LogInformation($"Starting gRPC server at {Uri}");
            _grpcServer.Start();
        }
    }
}
