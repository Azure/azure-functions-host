using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;

namespace Microsoft.WindowsAzure.Jobs.Dashboard
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{action}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );
        }
    }
}
