// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class AspNetCoreGrpcServerTests
    {
        [Fact]
        public void CleanDisposal()
        {
            var server = new AspNetCoreGrpcServer(new Mock<FunctionRpc.FunctionRpcBase>().Object, new TestScriptEventManager(), Mock.Of<IScriptHostManager>(), NullLogger<AspNetCoreGrpcServer>.Instance);
            server.Dispose();
        }

        [Fact]
        public async Task CleanDisposalAsync()
        {
            var server = new AspNetCoreGrpcServer(new Mock<FunctionRpc.FunctionRpcBase>().Object, new TestScriptEventManager(), Mock.Of<IScriptHostManager>(), NullLogger<AspNetCoreGrpcServer>.Instance);
            await server.DisposeAsync();
        }
    }
}
