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

        protected LanguageFunctionInvokerBase(ScriptHost host, FunctionMetadata functionMetadata, ITraceWriterFactory traceWriterFactory = null)
           : base(host, functionMetadata, traceWriterFactory)
        {
            _rpcFactory = new RpcFactory();
            this.rpc = _rpcFactory.CreateRpcClient(RpcConstants.GoogleRpc);

            // TODO make sure worker is initialized before sending load message
            // Need to wait for this call to complete before calling invocation
            // TODO store function_id of the loaded functions
            // Host.FunctionDispatcher.LoadAsync(Metadata);
        }

        protected IRpc GetRpcClient()
        {
            return this.rpc;
        }

        protected override abstract Task InvokeCore(object[] parameters, FunctionInvocationContext context);
    }
}
