// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Routing;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public class HttpRouteFactory
    {
        private readonly DirectRouteFactoryContext _routeFactoryContext;

        public HttpRouteFactory(string prefix)
        {
            var constraintResolver = new DefaultInlineConstraintResolver();
            List<HttpActionDescriptor> actionDescriptors = new List<HttpActionDescriptor>();
            _routeFactoryContext = new DirectRouteFactoryContext(prefix, actionDescriptors, constraintResolver, false);
        }

        public IDirectRouteBuilder CreateRouteBuilder(string routeTemplate)
        {
            return _routeFactoryContext.CreateBuilder(routeTemplate);
        }

        public IHttpRoute AddRoute(string routeName, string routeTemplate, HttpRouteCollection routes)
        {
            var routeBuilder = CreateRouteBuilder(routeTemplate);
            var httpRoute = routes.CreateRoute(routeBuilder.Template, routeBuilder.Defaults, routeBuilder.Constraints);
            routes.Add(routeName, httpRoute);

            return httpRoute;
        }

        public IEnumerable<string> GetRouteParameters(string routeTemplate)
        {
            var routeBuilder = CreateRouteBuilder(routeTemplate);
            return ParseRouteParameters(routeBuilder.Template);
        }

        private static IEnumerable<string> ParseRouteParameters(string routeTemplate)
        {
            List<string> routeParameters = new List<string>();

            if (!string.IsNullOrEmpty(routeTemplate))
            {
                string[] segments = routeTemplate.Split('/');
                foreach (string segment in segments)
                {
                    if (segment.StartsWith("{") && segment.EndsWith("}"))
                    {
                        string parameter = segment.Substring(1, segment.Length - 2);
                        routeParameters.Add(parameter);
                    }
                }
            }

            return routeParameters;
        }

    }
}
