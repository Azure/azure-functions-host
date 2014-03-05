using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using Dashboard.App_Start;
using Microsoft.WindowsAzure.Jobs;
using Ninject;

namespace Dashboard
{
    public class MvcApplication : HttpApplication
    {
        protected void Application_Start()
        {
            var kernel = NinjectWebCommon.Kernel;
            
            AreaRegistration.RegisterAllAreas();
                
            GlobalConfiguration.Configure(WebApiConfig.Register);
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);

            if (!SimpleBatchStuff.BadInit)
            {
                ModelBinderConfig.Register(kernel);
                HostVersionConfig.RegisterWarnings(kernel.Get<IHostVersionReader>());
            }
        }
    }    
}
