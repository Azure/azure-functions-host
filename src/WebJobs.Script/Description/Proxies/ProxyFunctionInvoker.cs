// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.AppService.Proxy.Client;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal class ProxyFunctionInvoker : FunctionInvokerBase
    {
        private ProxyClientExecutor _proxyClient;

        public ProxyFunctionInvoker(ScriptHost host, FunctionMetadata functionMetadata, ProxyClientExecutor proxyClient)
            : base(host, functionMetadata)
        {
            _proxyClient = proxyClient;
        }

        protected override async Task InvokeCore(object[] parameters, FunctionInvocationContext context)
        {
            object requestObj = (parameters != null && parameters.Any()) ? parameters[0] : null;

            if (requestObj == null || !(requestObj is HttpRequestMessage))
            {
                throw new Exception("Could not find parameter of type HttpRequestMessage while executing a Proxy Request");
            }

            await _proxyClient.Execute(requestObj as HttpRequestMessage, context.Logger);
        }
    }
}
