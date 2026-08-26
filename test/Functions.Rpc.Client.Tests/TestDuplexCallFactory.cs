// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Azure.Functions.Rpc.Client.Tests;

/// <summary>
/// Supplies a predetermined duplex call without creating network or gRPC resources.
/// </summary>
internal sealed class TestDuplexCallFactory<TRequest, TResponse> : IDuplexCallFactory<TRequest, TResponse>
    where TRequest : class
    where TResponse : class
{
    private readonly IDuplexCall<TRequest, TResponse> _call;

    internal TestDuplexCallFactory(IDuplexCall<TRequest, TResponse> call)
    {
        _call = call;
    }

    internal Uri Endpoint { get; private set; }

    internal int InvocationCount { get; private set; }

    /// <inheritdoc />
    public Task<IDuplexCall<TRequest, TResponse>> ConnectAsync(Uri endpoint, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Endpoint = endpoint;
        InvocationCount++;
        return Task.FromResult(_call);
    }
}
