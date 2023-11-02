// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
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

internal interface IInvocationMessageDispatcherFactory
{
    IInvocationMessageDispatcher Create(string invocationId);
}

internal class ThreadPoolInvocationProcessorFactory : IInvocationMessageDispatcher, IInvocationMessageDispatcherFactory
{
    private readonly WaitCallback _callback;

    public ThreadPoolInvocationProcessorFactory(WaitCallback callback)
    {
        _callback = callback;
    }

    // always return a single instance
    public IInvocationMessageDispatcher Create(string invocationId) => this;

    public void DispatchRpcLog(InboundGrpcEvent msg) => ThreadPool.QueueUserWorkItem(_callback, msg);

    public void DispatchInvocationResponse(InboundGrpcEvent msg) => ThreadPool.QueueUserWorkItem(_callback, msg);
}