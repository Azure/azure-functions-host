// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.Rpc.Messages;

namespace Microsoft.Azure.WebJobs.Script.RPC.Grpc
{
    internal class GrpcServer
    {
        private Server _server;

        public GrpcServer()
        {
            _server = new Server
            {
                Services = { FunctionRpc.BindService(new GoogleRpcServer()) },

                // TODO: port selection, encryption
                Ports = { new ServerPort("localhost", 0, ServerCredentials.Insecure) }
            };
        }

        public void Start() => _server.Start();

        public Task ShutdownAsync() => _server.ShutdownAsync();
    }
}
