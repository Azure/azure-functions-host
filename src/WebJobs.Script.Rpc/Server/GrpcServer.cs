// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Rpc.Messages;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{ 
    public class GrpcServer
    {
        private Server _server;
        private FunctionRpcImpl _serverImpl;

        // TODO: start in a new thread?
        public GrpcServer()
        {
            _serverImpl = new FunctionRpcImpl();
            _server = new Server
            {
                Services = { FunctionRpc.BindService(_serverImpl) },

                // TODO: port selection, encryption
                Ports = { new ServerPort("127.0.0.1", 50051, ServerCredentials.Insecure) }
            };
            
        }

        public IObservable<ChannelContext> Connections => _serverImpl.Connections;

        public void Start() => _server.Start();

        public Task ShutdownAsync() => _server.ShutdownAsync();
    }
}