// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Rpc.Messages;
using System.Linq;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{ 
    public class GrpcServer
    {
        private Server _server;
        private FunctionRpcImpl _serverImpl;

        public GrpcServer()
        {
            _serverImpl = new FunctionRpcImpl();
            _server = new Server
            {
                Services = { FunctionRpc.BindService(_serverImpl) },
                Ports = { new ServerPort("127.0.0.1", ServerPort.PickUnused, ServerCredentials.Insecure) }
            };
        }

        public IObservable<ChannelContext> Connections => _serverImpl.Connections;

        public void Start() => _server.Start();

        public Task ShutdownAsync() => _server.ShutdownAsync();

        public int BoundPort => _server.Ports.First().BoundPort;
    }
}