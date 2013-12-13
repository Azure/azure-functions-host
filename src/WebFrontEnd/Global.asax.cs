using System;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using DaasEndpoints;
using RunnerInterfaces;
using WebFrontEnd.App_Start;

namespace WebFrontEnd
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            EnableLogging();

            try
            {
                var kernel = NinjectWebCommon.Kernel;

                AreaRegistration.RegisterAllAreas();
                
                WebApiConfig.Register(GlobalConfiguration.Configuration);
                FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
                RouteConfig.RegisterRoutes(RouteTable.Routes);
                BundleConfig.RegisterBundles(BundleTable.Bundles);
                

                ModelBinderConfig.Register(kernel);
                HostVersionConfig.RegisterWarnings((IHostVersionReader)kernel.GetService(typeof(IHostVersionReader)));
            }
            catch (Exception e)
            {
                LogFatalError(e);
                throw;
            }
        }

        private void EnableLogging()
        {
            AppDomain a = AppDomain.CurrentDomain;
            a.UnhandledException += a_UnhandledException;
        }

        void a_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
        }

        void LogFatalError(Exception ex)
        {
            var s = GetServices();
            s.LogFatalError("From Web Role", ex);            
        }

        // This is a special case used just for logging errors. 
        private static Services GetServices()
        {
            AzureRoleAccountInfo accountInfo = new AzureRoleAccountInfo();
            return new Services(accountInfo);
        }
    }    
}