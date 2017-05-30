// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public abstract class LanguageFunctionInvokerBase : FunctionInvokerBase, ILanguageInvoker
    {
        private IRpc rpc;
        private RpcFactory _rpcFactory;

        protected LanguageFunctionInvokerBase(ScriptHost host, FunctionMetadata functionMetadata, ITraceWriterFactory traceWriterFactory = null)
           : base(host, functionMetadata, traceWriterFactory)
        {
            _rpcFactory = new RpcFactory();
            this.rpc = _rpcFactory.CreateRpcClient(RpcConstants.GoogleRpc);
            Host.FunctionDispatcher.LoadAsync(Metadata);
        }

        // public event EventHandler<LanguageInvokerMessagesEventArgs> LanguageInvokerMessagesUpdated;

        protected IRpc GetRpcClient()
        {
            return this.rpc;
        }

        protected override abstract Task InvokeCore(object[] parameters, FunctionInvocationContext context);
    }
}
