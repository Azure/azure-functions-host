// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using Microsoft.Azure.WebJobs.Script.Grpc.Eventing;

namespace Microsoft.Azure.WebJobs.Script.Grpc;

/// <summary>
/// Temporary class that handles both creation of the processor and acts as the processor itself. Once we've
/// confirmed that the OrderedInvocationMessageDispatcher works as expected, we will remove this.
/// </summary>
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