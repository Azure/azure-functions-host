// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Grpc.Eventing;

namespace Microsoft.Azure.WebJobs.Script.Grpc;

/// <summary>
/// Interface for processing grpc messages that may come from the worker that are
/// related to an invocation (RpcLog and InvocationResponse). The contract here is as-follows:
/// - The contract with a worker is that, during an invocation (i.e an InvocationRequest has been sent), there are only
///   two messages that the worker can send us related to that invocation
///   - one or many RpcLog messages
///   - one final InvocationResponse that effectively ends the invocation. Once the InvocationResponse is received, no
///     more RpcLog messages from this specific invocation will be processed.
/// - The GrpcChannel is looping to dequeue grpc messages from a worker. When it finds one that is either an RpcLog
///   or an InvocationResponse, it will will call the matching method on this interface (i.e. RpcLog -> DispatchRpcLog)
///   from the same thread that is looping and dequeuing items from the grpc channel. The implementors of this interface
///   must quickly dispatch the message to a background Task or Thread for handling, so as to not block the
///   main loop from dequeuing more messages.
/// - Because the methods on this interface are all on being called from the same thread, they do not need to be
///   thread-safe. They can assume that they will not be called multiple times from different threads.
/// </summary>
internal interface IInvocationMessageDispatcher
{
    /// <summary>
    /// Inspects the incoming RpcLog and dispatches to a Thread or background Task as quickly as possible. This method is
    /// called from a loop processing incoming grpc messages and any thread blocking will delay the processing of that loop.
    /// It can be assumed that this method will never be called from multiple threads simultaneously and thus does not need
    /// to be thread-safe.
    /// </summary>
    /// <param name="msg">The RpcLog message. Implementors can assume that this message is an RpcLog.</param>
    void DispatchRpcLog(InboundGrpcEvent msg);

    /// <summary>
    /// Inspects an incoming InvocationResponse message and dispatches to a Thread or background Task as quickly as possible.
    /// This method is called from a loop processing incoming grpc messages and any thread blocking will delay the processing
    /// of that loop. It can be assumed that this method will never be called from multiple threads simultaneously and thus
    /// does not need to be thread-safe.
    /// </summary>
    /// <param name="msg">The InvocationResponse message. Implementors can assume that this message is an InvocationResponse.</param>
    void DispatchInvocationResponse(InboundGrpcEvent msg);
}