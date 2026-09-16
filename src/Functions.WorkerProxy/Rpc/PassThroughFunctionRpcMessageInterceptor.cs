// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Azure.Functions.WorkerProxy.Rpc;

internal sealed class PassThroughFunctionRpcMessageInterceptor : IFunctionRpcMessageInterceptor
{
    public ValueTask<FunctionRpcMessageDisposition> ProcessAsync(
        FunctionRpcRelaySide side,
        StreamingMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        return ValueTask.FromResult(FunctionRpcMessageDisposition.Forward);
    }
}