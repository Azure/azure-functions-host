// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Azure.Functions.Rpc.Client.Tests;

/// <summary>
/// Supplies duplex channels without creating network or gRPC resources.
/// </summary>
internal sealed class TestDuplexChannelFactory<T> : IDuplexChannelFactory<T>
    where T : class
{
    private readonly Func<Channel<T>> _channelFactory;

    internal TestDuplexChannelFactory(Func<Channel<T>> channelFactory)
    {
        _channelFactory = channelFactory ?? throw new ArgumentNullException(nameof(channelFactory));
    }

    internal Uri Endpoint { get; private set; }

    internal int InvocationCount { get; private set; }

    /// <inheritdoc />
    public Task<Channel<T>> ConnectAsync(Uri endpoint, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Endpoint = endpoint;
        InvocationCount++;
        return Task.FromResult(_channelFactory());
    }
}
