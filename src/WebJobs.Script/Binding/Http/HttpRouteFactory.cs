// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System;
using System.Linq;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Routing;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public class HttpRouteFactory
    {
        private readonly DirectRouteFactoryContext _routeFactoryContext;

        public HttpRouteFactory(string prefix = "api")
        {
            var constraintResolver = new DefaultInlineConstraintResolver();
            List<HttpActionDescriptor> actionDescriptors = new List<HttpActionDescriptor>();
            _routeFactoryContext = new DirectRouteFactoryContext(prefix, actionDescriptors, constraintResolver, false);
        }

        public IDirectRouteBuilder CreateRouteBuilder(string routeTemplate)
        {
            return _routeFactoryContext.CreateBuilder(routeTemplate);
        }

        public bool TryAddRoute(string routeName, string routeTemplate, IEnumerable<HttpMethod> methods, HttpRouteCollection routes, out IHttpRoute route)
        {
            route = null;

            try
            {
                var routeBuilder = CreateRouteBuilder(routeTemplate);
                var constraints = routeBuilder.Constraints;
                if (methods != null)
                {
                    // if the methods collection is not null, apply the constraint
                    // if the methods collection is empty, we'll create a constraint
                    // that disallows ALL methods
                    constraints.Add("httpMethod", new HttpMethodConstraint(methods.ToArray()));
                }
                route = routes.CreateRoute(routeBuilder.Template, routeBuilder.Defaults, constraints);
                routes.Add(routeName, route);
            }
            catch (Exception ex) when (!ex.IsFatal()) 
            {
                // catch any route parsing errors
                return false;
            }

            return true;
        }

        public IEnumerable<string> GetRouteParameters(string routeTemplate)
        {
            var routeBuilder = CreateRouteBuilder(routeTemplate);

            // this template will have any inline constraints parsed
            // out at this point
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
