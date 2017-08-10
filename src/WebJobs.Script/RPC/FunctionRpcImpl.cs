// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Eventing;
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
                _eventManager.OfType<RpcEvent>()
                    .Where(evt => evt.Origin == RpcEvent.MessageOrigin.Host && evt.WorkerId == workerId)

                    // TODO: correctly handle async writes?
                    .Subscribe(evt =>
                    {
                        responseStream.WriteAsync(evt.Message);
                    });

                do
                {
                    _eventManager.Publish(new RpcEvent(workerId, requestStream.Current, RpcEvent.MessageOrigin.Worker));
                }
                while (await requestStream.MoveNext(CancellationToken.None));
            }
        }
    }
}
