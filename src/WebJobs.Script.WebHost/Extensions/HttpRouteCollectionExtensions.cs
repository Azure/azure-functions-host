// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Routing;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Extensions
{
    public static class HttpRouteCollectionExtensions
    {
        public static IHttpRouteData GetRouteData(this HttpRouteCollection routes, HttpRequestMessage request, bool proxyRoutesFirst = true)
        {
            if (!proxyRoutesFirst)
            {
                // order proxy routes last
                var orderedRoutes = routes.OrderBy(p => ((FunctionDescriptor)p.DataTokens[ScriptConstants.AzureFunctionsHttpFunctionKey]).Metadata.IsProxy ? 1 : 0);
                return orderedRoutes.Select(p => p.GetRouteData(routes.VirtualPathRoot, request)).FirstOrDefault(p => p != null);
            }
            else
            {
                // do the default query
                return routes.GetRouteData(request);
            }
        }
    }
}