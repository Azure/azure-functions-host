// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.AppService.AdvancedRouting.Proxy.Gateway;

namespace Microsoft.Azure.WebJobs.Script
{
    public class FunctionExecutor : IFuncExecutor
    {
        private JobHostConfiguration _config;
        private ScriptHost _host;

        internal FunctionExecutor(JobHostConfiguration config, ScriptHost host)
        {
            _config = config;
            _host = host;
        }

        public async Task ExecuteFuncAsync(string funcName, Dictionary<string, object> arguments, CancellationToken cancellationToken = default(CancellationToken))
        {
            Type type = _config.TypeLocator.GetTypes().SingleOrDefault(p => p.Name == ScriptHost.GeneratedTypeName);

            var methodInfo = type.GetMethods().SingleOrDefault(p => p.Name.ToLowerInvariant() == funcName.ToLowerInvariant());

            if (methodInfo == null)
            {
                // throw new ArgumentException("${funcName} function not found!");
                // TODO: don't throw an exception for now.
                return;
            }

            var firstParam = methodInfo?.GetParameters()?.FirstOrDefault();
            if (firstParam != null && firstParam.ParameterType == typeof(HttpRequestMessage))
            {
                arguments.Add(firstParam.Name, arguments[ScriptConstants.AzureFunctionsProxyHttpRequestKey]);
            }
            else if (firstParam != null && firstParam.ParameterType == typeof(HttpResponseMessage))
            {
                arguments.Add(firstParam.Name, arguments[ScriptConstants.AzureFunctionsHttpResponseKey]);
            }

            await _host.CallAsync(methodInfo, arguments, cancellationToken);
        }
    }
}