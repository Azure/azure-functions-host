﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal class ProxyFunctionInvoker : FunctionInvokerBase
    {
        private ProxyClientExecutor _proxyClient;

        public ProxyFunctionInvoker(ScriptHost host, ProxyFunctionMetadata proxyfunctionMetadata, ILoggerFactory loggerFactory)
            : base(host, proxyfunctionMetadata, loggerFactory)
        {
            _proxyClient = proxyfunctionMetadata.ProxyClient;
        }

        protected override async Task<object> InvokeCore(object[] parameters, FunctionInvocationContext context)
        {
            if (parameters?.FirstOrDefault() is not HttpRequest requestObj)
            {
                throw new Exception("Could not find parameter of type HttpRequest while executing a Proxy Request");
            }

            await _proxyClient.Execute(requestObj, context.Logger);
            return requestObj.HttpContext.Items[ScriptConstants.AzureFunctionsHttpResponseKey];
        }
    }
}
