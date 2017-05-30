// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.Rpc.Messages;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public abstract class LanguageFunctionInvokerBase : FunctionInvokerBase
    {
        private IRpc rpc;
        private RpcFactory _rpcFactory;
        private IDisposable _logSubscription;

        protected LanguageFunctionInvokerBase(ScriptHost host, FunctionMetadata functionMetadata, ITraceWriterFactory traceWriterFactory = null)
           : base(host, functionMetadata, traceWriterFactory)
        {
            _rpcFactory = new RpcFactory();
            this.rpc = _rpcFactory.CreateRpcClient(RpcConstants.GoogleRpc);
            InitializeRpcStreamLogWatcher();
            Host.FunctionDispatcher.LoadAsync(Metadata);
        }

        // public event EventHandler<LanguageInvokerMessagesEventArgs> LanguageInvokerMessagesUpdated;

        protected IRpc GetRpcClient()
        {
            return this.rpc;
        }

        protected void InitializeRpcStreamLogWatcher()
        {
            _logSubscription = Host.EventManager.OfType<RpcMessageEvent>()
                      .Where(rpcMessageEvent => rpcMessageEvent.RpcMessageArguments.Message.Type == StreamingMessage.Types.Type.Log)
            .Subscribe(rpcMessageEvent => OnLogMessageReceived(null, rpcMessageEvent.RpcMessageArguments));
        }

        private object OnLogMessageReceived(object p, object rpcMessageArguments)
        {
            // TODO write to SystemTraceWriter
            throw new NotImplementedException();
        }

        protected override abstract Task InvokeCore(object[] parameters, FunctionInvocationContext context);
    }
}
