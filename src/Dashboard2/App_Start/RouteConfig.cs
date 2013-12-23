using System.Web.Mvc;
using System.Web.Routing;

namespace Dashboard
{
    public class RouteConfig
    {
        public static void RegisterRoutes(RouteCollection routes)
        {
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

            if (SimpleBatchStuff.BadInit)
            {
                routes.MapRoute(
                name: "Default",
                url: "{a}/{b}/{id}",
                defaults: new
                    {
                        controller = "BadConfig",
                        action = "Index",
                        a = UrlParameter.Optional,
                        b = UrlParameter.Optional,
                        id = UrlParameter.Optional
                    }
                );

                return;
            }

            routes.MapRoute(
                name: "Default",
                url: "{controller}/{action}/{id}",
                //defaults: new { controller = "Home", action = "Index", id = UrlParameter.Optional }
                defaults: new { controller = "Dashboard", action = "Index", id = UrlParameter.Optional }
            );
        }
    }
}
