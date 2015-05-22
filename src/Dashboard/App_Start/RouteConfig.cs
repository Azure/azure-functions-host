// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using Dashboard.Controllers;

namespace Dashboard
{
    public static class RouteConfig
    {
        public static void RegisterRoutes(RouteCollection routes)
        {
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

            routes.MapMvcAttributeRoutes();
        }

        public static void RegisterNoAccountRoutes(RouteCollection routes)
        {
            if (routes == null)
            {
                throw new ArgumentNullException("routes");
            }

            RegisterRoutes(routes);

            // deregister the non SPA MVC route, and replace it with a redirect to Functions Homepage so
            // the user would get the helpful error.
            var legacyRoute = routes.OfType<Route>().First(r => r.Url == FunctionController.LegacyNonSpaRouteUrl);
            routes.Remove(legacyRoute);
            routes.Add(new Route(FunctionController.LegacyNonSpaRouteUrl, new RedirectRouteHandler("~/#/functions")));
        }

        // a Redirect route handler. We only need this as a stopgap until we SPA-ify the SearchBlob and Run/Replay pages
        private class RedirectRouteHandler : IRouteHandler
        {
            private readonly string _targetUrl;

            public RedirectRouteHandler(string targetUrl)
            {
                _targetUrl = targetUrl;
            }

            public IHttpHandler GetHttpHandler(RequestContext requestContext)
            {
                return new RedirectHandler(_targetUrl);
            }

            private class RedirectHandler : IHttpHandler
            {
                private readonly string _targetUrl;

                public RedirectHandler(string targetUrl)
                {
                    _targetUrl = targetUrl;
                }

                public bool IsReusable
                {
                    get { return false; }
                }

                public void ProcessRequest(HttpContext context)
                {
                    if (context == null)
                    {
                        throw new ArgumentNullException("context");
                    }

                    context.Response.Redirect(UrlHelper.GenerateContentUrl(_targetUrl, new HttpContextWrapper(context)));
                }
            }
        }
    }
}
