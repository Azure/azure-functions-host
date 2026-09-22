// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Azure.Functions.WorkerProxy.Rpc;

internal interface IFunctionRpcMessageInterceptor
{
    ValueTask<FunctionRpcMessageDisposition> ProcessAsync(
        FunctionRpcRelaySide side,
        StreamingMessage message,
        CancellationToken cancellationToken);
}