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
                url: "{*path}",
                defaults: new
                    {
                        controller = "BadConfig",
                        action = "Index"
                    }
                );

                return;
            }

            routes.MapRoute(
                name: "Default",
                url: "{controller}/{action}/{id}",
                defaults: new { controller = "Main", action = "Index", id = UrlParameter.Optional }
            );
        }
    }
}
