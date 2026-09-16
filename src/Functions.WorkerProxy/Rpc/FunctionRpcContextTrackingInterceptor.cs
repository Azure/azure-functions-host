// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Azure.Functions.WorkerProxy.Rpc;

internal sealed class FunctionRpcContextTrackingInterceptor : IFunctionRpcMessageInterceptor
{
    private readonly Lock _syncLock = new();
    private readonly Dictionary<string, FunctionRpcFunctionContext> _functions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FunctionRpcInvocationContext> _invocations = new(StringComparer.Ordinal);
    private FunctionRpcWorkerContext _worker = new(WorkerId: null, WorkerRuntime: null);

    public FunctionRpcWorkerContext Worker
    {
        get
        {
            lock (_syncLock)
            {
                return _worker;
            }
        }
    }

    public bool TryGetFunction(string functionId, out FunctionRpcFunctionContext? function)
    {
        ArgumentNullException.ThrowIfNull(functionId);

        lock (_syncLock)
        {
            return _functions.TryGetValue(functionId, out function);
        }
    }

    public bool TryGetInvocation(string invocationId, out FunctionRpcInvocationContext? invocation)
    {
        ArgumentNullException.ThrowIfNull(invocationId);

        lock (_syncLock)
        {
            return _invocations.TryGetValue(invocationId, out invocation);
        }
    }

    public ValueTask<FunctionRpcMessageDisposition> ProcessAsync(
        FunctionRpcRelaySide side,
        StreamingMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncLock)
        {
            if (side is FunctionRpcRelaySide.Runtime)
            {
                ProcessRuntimeMessage(message);
            }
            else if (side is FunctionRpcRelaySide.Worker)
            {
                ProcessWorkerMessage(message);
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown relay side.");
            }
        }

        return ValueTask.FromResult(FunctionRpcMessageDisposition.Forward);
    }

    private void ProcessRuntimeMessage(StreamingMessage message)
    {
        switch (message.ContentCase)
        {
            case StreamingMessage.ContentOneofCase.FunctionLoadRequest:
                RegisterFunction(message.FunctionLoadRequest);
                break;
            case StreamingMessage.ContentOneofCase.FunctionLoadRequestCollection:
                foreach (FunctionLoadRequest loadRequest in message.FunctionLoadRequestCollection.FunctionLoadRequests)
                {
                    RegisterFunction(loadRequest);
                }

                break;
            case StreamingMessage.ContentOneofCase.InvocationRequest:
                InvocationRequest invocationRequest = message.InvocationRequest;
                _functions.TryGetValue(invocationRequest.FunctionId, out FunctionRpcFunctionContext? function);
                _invocations[invocationRequest.InvocationId] = new FunctionRpcInvocationContext(
                    invocationRequest.InvocationId,
                    invocationRequest.FunctionId,
                    function?.FunctionName,
                    invocationRequest.TraceContext?.TraceParent,
                    invocationRequest.TraceContext?.TraceState);
                break;
        }
    }

    private void ProcessWorkerMessage(StreamingMessage message)
    {
        switch (message.ContentCase)
        {
            case StreamingMessage.ContentOneofCase.StartStream:
                _worker = _worker with { WorkerId = message.StartStream.WorkerId };
                break;
            case StreamingMessage.ContentOneofCase.WorkerInitResponse
                when message.WorkerInitResponse.Result?.Status is StatusResult.Types.Status.Success:
                _worker = _worker with { WorkerRuntime = message.WorkerInitResponse.WorkerMetadata?.RuntimeName };
                break;
            case StreamingMessage.ContentOneofCase.InvocationResponse:
                _invocations.Remove(message.InvocationResponse.InvocationId);
                break;
        }
    }

    private void RegisterFunction(FunctionLoadRequest request)
    {
        _functions[request.FunctionId] = new FunctionRpcFunctionContext(request.FunctionId, request.Metadata?.Name ?? string.Empty);
    }
}