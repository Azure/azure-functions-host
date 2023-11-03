// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Grpc.Eventing;

namespace Microsoft.Azure.WebJobs.Script.Grpc;

/// <summary>
/// Interface for processing grpc messages that may come from the worker that are
/// related to an invocation (RpcLog and InvocationResponse).
/// </summary>
internal interface IInvocationMessageDispatcher
{
    void DispatchRpcLog(InboundGrpcEvent msg);

    void DispatchInvocationResponse(InboundGrpcEvent msg);
}