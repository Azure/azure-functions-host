// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public class RpcClientConnectionFactoryTests
{
    [Fact]
    public async Task ConnectUsesInjectedDuplexCallFactory()
    {
        TestDuplexCall<StreamingMessage, StreamingMessage> call = new();
        TestDuplexCallFactory<StreamingMessage, StreamingMessage> callFactory = new(call);
        IRpcClientConnectionFactory factory = new RpcClientConnectionFactory(callFactory, NullLogger<RpcClientConnection>.Instance);
        RpcClientConnectionOptions options = new(new Uri("https://localhost:5001"), "worker-1");

        await using RpcClientConnection connection = await factory.ConnectAsync(options);
        await connection.EnqueueAsync(new StreamingMessage { RequestId = "outbound" });
        await call.SendResponseAsync(new StreamingMessage { RequestId = "inbound" });
        call.CompleteResponses();
        List<string> responses = [];
        await foreach (StreamingMessage response in connection.ReadAllAsync())
        {
            responses.Add(response.RequestId);
        }

        Assert.Equal(options.Endpoint, callFactory.Endpoint);
        Assert.Equal(1, callFactory.InvocationCount);
        Assert.Equal("worker-1", connection.WorkerId);
        Assert.Equal(["outbound"], call.WrittenMessages.Select(message => message.RequestId));
        Assert.Equal(["inbound"], responses);
    }
}
