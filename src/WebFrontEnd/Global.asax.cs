using System;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using Microsoft.WindowsAzure.Jobs.Dashboard.App_Start;
using Ninject;

namespace Microsoft.WindowsAzure.Jobs.Dashboard
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            var kernel = NinjectWebCommon.Kernel;
            
            AreaRegistration.RegisterAllAreas();
                
            WebApiConfig.Register(GlobalConfiguration.Configuration);
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