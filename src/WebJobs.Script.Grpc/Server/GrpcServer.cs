// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{ 
    public class GrpcServer
    {
        private Server _server;

        public GrpcServer(FunctionRpc.FunctionRpcBase serviceImpl)
        {
            _server = new Server
            {
                Services = { FunctionRpc.BindService(serviceImpl) },
                Ports = { new ServerPort("127.0.0.1", ServerPort.PickUnused, ServerCredentials.Insecure) }
            };
        }

        public void Start() => _server.Start();

        public Task ShutdownAsync() => _server.ShutdownAsync();

        public int BoundPort => _server.Ports.First().BoundPort;
    }
}