// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Workers;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Provides the invocation dispatcher for client-backed workers.
/// </summary>
internal sealed class RpcClientFunctionInvocationDispatcherFactory(
    IRpcClientFunctionInvocationDispatcher dispatcher) : IFunctionInvocationDispatcherFactory
{
    private readonly IRpcClientFunctionInvocationDispatcher _dispatcher =
        dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    public IFunctionInvocationDispatcher GetFunctionDispatcher() => _dispatcher;
}
