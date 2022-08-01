// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Grpc.Core;
using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using WorkerHarness.Core.Commons;

namespace WorkerHarness.Core.GrpcService
{
    public class GrpcServer : IGrpcServer
    {
        private readonly Server _grpcServer;

        public GrpcServer(GrpcServiceChannel channel)
        {
            int port = WorkerUtilities.GetUnusedTcpPort();
            Uri = new Uri($"http://{HostConstants.HostName}:{port}");

            GrpcService grpcService = new(channel.InboundChannel, channel.OutboundChannel);
            _grpcServer = new()
            {
                Services = { FunctionRpc.BindService(grpcService) },
                Ports = { new ServerPort(Uri.Host, Uri.Port, ServerCredentials.Insecure) }
            };
        }
        public Uri Uri { get; }

        public async Task Shutdown()
        {
            await _grpcServer.ShutdownAsync();
        }

        public void Start()
        {
            _grpcServer.Start();
        }
    }
}
