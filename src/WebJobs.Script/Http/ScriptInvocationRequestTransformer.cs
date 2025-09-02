// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Yarp.ReverseProxy.Forwarder;

namespace Microsoft.Azure.WebJobs.Script.Http
{
    internal sealed class ScriptInvocationRequestTransformer : HttpTransformer
    {
        public static readonly ScriptInvocationRequestTransformer Instance = new ScriptInvocationRequestTransformer();

        private ScriptInvocationRequestTransformer() { }

        public override async ValueTask TransformRequestAsync(HttpContext httpContext, HttpRequestMessage proxyRequest, string destinationPrefix, CancellationToken cancellationToken)
        {
            // this preserves previous behavior (which called the default transformer) - base method is also called inside of here
            await HttpTransformer.Default.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix, cancellationToken);

            if (httpContext.Items.TryGetValue(ScriptConstants.HttpProxyScriptInvocationContext, out object result)
                && result is ScriptInvocationContext scriptContext)
            {
                proxyRequest.Options.TryAdd(ScriptConstants.HttpProxyScriptInvocationContext, scriptContext);
            }
        }
    }
}
