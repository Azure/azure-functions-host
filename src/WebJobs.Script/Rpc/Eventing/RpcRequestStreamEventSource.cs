// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Rpc.Messages;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public sealed class RpcRequestStreamEventSource : IDisposable
    {
        private readonly RpcRequestStreamWatcher _rpcMessageWatcher;
        private readonly IScriptEventManager _eventManager;
        private bool _disposed = false;

        public RpcRequestStreamEventSource(IScriptEventManager eventManager, string source,
            StreamingMessage.Types.Type rpcMessageType)
        {
            _eventManager = eventManager;
            _rpcMessageWatcher = new RpcRequestStreamWatcher();
            _rpcMessageWatcher.RpcMessageReceived += RpcMessageHandler;
        }

        public void RpcMessageHandler(object sender, RpcMessageReceivedEventArgs eventArgs)
        {
            var rpcMessageEvent = new RpcMessageEvent(eventArgs.Message.Type.ToString(), "RpcRequestMessage", eventArgs);
            _eventManager.Publish(rpcMessageEvent);
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _rpcMessageWatcher.Dispose();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
