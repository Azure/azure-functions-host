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
            if (await requestStream.MoveNext(CancellationToken.None))
            {
                string workerId = requestStream.Current.StartStream.WorkerId;
                _eventManager.OfType<OutboundEvent>()
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
                while (await requestStream.MoveNext(CancellationToken.None));
            }
        }
    }
}
