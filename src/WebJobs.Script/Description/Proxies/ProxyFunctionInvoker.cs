// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.AppService.Proxy.Client.Contract;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal class ProxyFunctionInvoker : FunctionInvokerBase
    {
        private IProxyClient _proxyClient;

        public ProxyFunctionInvoker(ScriptHost host, FunctionMetadata functionMetadata, IProxyClient proxyClient)
            : base(host, functionMetadata, new FunctionLogger(host, functionMetadata.Name, logDirName: "Proxy"))
        {
            _proxyClient = proxyClient;
        }

        protected override async Task InvokeCore(object[] parameters, FunctionInvocationContext context)
        {
            await _proxyClient.CallAsync(parameters, null, context.Logger);
        }
    }
}
