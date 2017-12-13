// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Routing;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Extensions.Http
{
    public static class HttpRouteFactoryExtensions
    {
        public static bool TryAddRoute(this HttpRouteFactory httpRouteFactory, HttpRouteCollection httpRoutes, FunctionDescriptor function)
        {
            var httpTrigger = function.GetTriggerAttributeOrNull<HttpTriggerAttribute>();
            if (httpTrigger != null)
            {
                IHttpRoute httpRoute = null;
                IEnumerable<HttpMethod> httpMethods = null;
                if (httpTrigger.Methods != null)
                {
                    httpMethods = httpTrigger.Methods.Select(p => new HttpMethod(p)).ToArray();
                }
                var dataTokens = new Dictionary<string, object>
                {
                    { ScriptConstants.AzureFunctionsHttpFunctionKey, function }
                };
                return httpRouteFactory.TryAddRoute(function.Metadata.Name, httpTrigger.Route, httpMethods, dataTokens, httpRoutes, out httpRoute);
            }

            return false;
        }
    }
}