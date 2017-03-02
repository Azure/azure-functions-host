// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public class ZeroMQ : IRpc
    {
        public ZeroMQ()
        {
            // Set up sockets
            throw new NotImplementedException();
        }

        public string GetRpcProvider()
        {
            return RpcConstants.ZeroMQ;
        }

        public Task<LanguageInvokerInitializationResult> SetupNodeRpcWorker(TraceWriter systemTraceWriter)
        {
            throw new NotImplementedException();
        }

        public Task<LanguageInvokerInitializationResult> SetupDotNetRpcWorker(TraceWriter systemTraceWriter)
        {
            throw new NotImplementedException();
        }

        public Task<object> SendMessageToRpcWorker(ScriptType scriptType, string scriptFilePath, object[] parameters, FunctionInvocationContext context, Dictionary<string, object> scriptExecutionContext)
        {
            throw new NotImplementedException();
        }
    }
}
