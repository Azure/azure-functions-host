// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Eventing.Rpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;

using MsgType = Microsoft.Azure.WebJobs.Script.Grpc.Messages.StreamingMessage.ContentOneofCase;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    // Implementation for the grpc service
    // TODO: move to WebJobs.Script.Grpc package and provide event stream abstraction
    internal class FunctionRpcService : FunctionRpc.FunctionRpcBase
    {
        private readonly IScriptEventManager _eventManager;
        private readonly ILogger _logger;

        public FunctionRpcService(IScriptEventManager eventManager, ILogger<FunctionRpcService> logger)
        {
            _eventManager = eventManager;
            _logger = logger;
        }

        public override async Task EventStream(IAsyncStreamReader<StreamingMessage> requestStream, IServerStreamWriter<StreamingMessage> responseStream, ServerCallContext context)
        {
            var cancelSource = new TaskCompletionSource<bool>();
            IDictionary<string, IDisposable> outboundEventSubscriptions = new Dictionary<string, IDisposable>();
            try
            {
                context.CancellationToken.Register(() => cancelSource.TrySetResult(false));
                Func<Task<bool>> messageAvailable = async () =>
                {
                    // GRPC does not accept cancellation tokens for individual reads, hence wrapper
                    var requestTask = requestStream.MoveNext(CancellationToken.None);
                    var completed = await Task.WhenAny(cancelSource.Task, requestTask);
                    return completed.Result;
                };

                if (await messageAvailable())
                {
                    string workerId = requestStream.Current.StartStream.WorkerId;
                    _logger.LogDebug("Established RPC channel. WorkerId: {workerId}", workerId);
                    outboundEventSubscriptions.Add(workerId, _eventManager.OfType<OutboundEvent>()
                        .Where(evt => evt.WorkerId == workerId)
                        .ObserveOn(NewThreadScheduler.Default)
                        .Subscribe(evt =>
                        {
                            try
                            {
                                // WriteAsync only allows one pending write at a time
                                // For each responseStream subscription, observe as a blocking write, in series, on a new thread
                                if (evt.MessageType == MsgType.InvocationRequest)
                                {
                                    _logger.LogDebug("Writing invocation request invocationId: {invocationId} to workerId: {workerId}", evt.Message.InvocationRequest.InvocationId, workerId);
                                }
                                responseStream.WriteAsync(evt.Message).GetAwaiter().GetResult();
                            }
                            catch (Exception subscribeEventEx)
                            {
                                _logger.LogError(subscribeEventEx, "Error writing message type {messageType} to workerId: {workerId}", evt.MessageType, workerId);
                            }
                        }));
                    do
                    {
                        var currentMessage = requestStream.Current;
                        if (currentMessage.InvocationResponse != null && !string.IsNullOrEmpty(currentMessage.InvocationResponse.InvocationId))
                        {
                            _logger.LogDebug("Received invocation response for invocationId: {invocationId} from workerId: {workerId}", currentMessage.InvocationResponse.InvocationId, workerId);
                        }
                        _eventManager.Publish(new InboundEvent(workerId, currentMessage));
                    }
                    while (await messageAvailable());
                }
            }
            finally
            {
                foreach (var sub in outboundEventSubscriptions)
                {
                    sub.Value?.Dispose();
                }

                // ensure cancellationSource task completes
                cancelSource.TrySetResult(false);
            }
        }
    }
}
