// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public class DuplexChannelFactoryTests
{
    [Fact]
    public async Task TestFactoryCreatesReplaceableDuplexChannel()
    {
        TestDuplexChannel<StreamingMessage> expected = new();
        TestDuplexChannelFactory<StreamingMessage> factory = new(() => expected);
        Uri endpoint = new("https://localhost:5001");
        IReadOnlyDictionary<string, string> headers = new Dictionary<string, string> { ["x-test-header"] = "value" };

        DuplexChannel<StreamingMessage> actual = await factory.ConnectAsync(endpoint, headers);
        await actual.Writer.WriteAsync(new StreamingMessage { RequestId = "outbound" });
        StreamingMessage outbound = await expected.Requests.ReadAsync();
        await expected.SendResponseAsync(new StreamingMessage { RequestId = "inbound" });
        StreamingMessage inbound = await actual.Reader.ReadAsync();

        Assert.Same(expected, actual);
        Assert.Equal(endpoint, factory.Endpoint);
        Assert.Same(headers, factory.Headers);
        Assert.Equal(1, factory.InvocationCount);
        Assert.Equal("outbound", outbound.RequestId);
        Assert.Equal("inbound", inbound.RequestId);
    }

    [Fact]
    public async Task TestFactoryInvokesProviderForEachConnection()
    {
        TestDuplexChannelFactory<StreamingMessage> factory = new(() => new TestDuplexChannel<StreamingMessage>());
        Uri endpoint = new("https://localhost:5001");

        DuplexChannel<StreamingMessage> first = await factory.ConnectAsync(endpoint);
        DuplexChannel<StreamingMessage> second = await factory.ConnectAsync(endpoint);

        Assert.NotSame(first, second);
        Assert.Equal(2, factory.InvocationCount);
    }
}
