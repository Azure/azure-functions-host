using System;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using Dashboard.App_Start;
using Dashboard.Indexers;
using Microsoft.WindowsAzure.Jobs;
using Ninject;

namespace Dashboard
{
    public class MvcApplication : HttpApplication
    {
        private static IIndexer _indexer;

        protected void Application_Start()
        {
            var kernel = NinjectWebCommon.Kernel;

            AreaRegistration.RegisterAllAreas();

            GlobalConfiguration.Configure(WebApiConfig.Register);
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);

            if (!SdkSetupState.BadInit)
            {
                ModelBinderConfig.Register(kernel);
                HostVersionConfig.RegisterWarnings(kernel.Get<IHostVersionReader>());
            }

            _indexer = kernel.TryGet<IIndexer>();

            BeginRequest += Application_BeginRequest;
        }

        void Application_BeginRequest(object sender, EventArgs e)
        {
            if (_indexer != null)
            {
                try
                {
                    _indexer.Update();
                }
                catch (Exception exception)
                {
                    // Don't fail to display any dashboard data just because of an index updating error
                    // (could be transient).
                    HttpContext.Current.Items["IndexingException"] = exception.Message;
                }
            }
        }
    }
}
