// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Eventing.Rpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class FunctionRpcImpl : FunctionRpc.FunctionRpcBase
    {
        private readonly IScriptEventManager _eventManager;

        public FunctionRpcImpl(IScriptEventManager eventManager)
        {
            _eventManager = eventManager;
        }

        public override async Task EventStream(IAsyncStreamReader<StreamingMessage> requestStream, IServerStreamWriter<StreamingMessage> responseStream, ServerCallContext context)
        {
            var cancelSource = new TaskCompletionSource<bool>();
            IDisposable outboundEventSubscription = null;
            try
            {
                context.CancellationToken.Register(() => cancelSource.SetResult(false));

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
                    outboundEventSubscription = _eventManager.OfType<OutboundEvent>()
                        .Where(evt => evt.WorkerId == workerId)
                        .ObserveOn(NewThreadScheduler.Default)
                        .Subscribe(evt =>
                        {
                            // WriteAsync only allows one pending write at a time
                            // For each responseStream subscription, observe as a blocking write, in series, on a new thread
                            // Alternatives - could wrap responseStream.WriteAsync with a SemaphoreSlim to control concurrent access
                            responseStream.WriteAsync(evt.Message).GetAwaiter().GetResult();
                        });

                    do
                    {
                        _eventManager.Publish(new InboundEvent(workerId, requestStream.Current));
                    }
                    while (await messageAvailable());
                }
            }
            catch (Exception exc)
            {
                // TODO: do this properly so it can be associated with workerid, if possible
                _eventManager.Publish(new WorkerErrorEvent(this, exc));
            }
            finally
            {
                outboundEventSubscription?.Dispose();

                // ensure cancellationSource task completes
                cancelSource.TrySetResult(false);
            }
        }
    }
}
