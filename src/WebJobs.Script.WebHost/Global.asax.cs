using System;
using System.Web.Http;

namespace WebJobs.Script.WebHost
{
    public class WebApiApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            GlobalConfiguration.Configure(WebApiConfig.Register);
        }

        void Application_Error(object sender, EventArgs e)
        {
            // TODO: Log any unhandled exceptions
            Exception ex = Server.GetLastError();
        }

        void Application_End(object sender, EventArgs e)
        {
            WebScriptHostManager webScriptHostManager = (WebScriptHostManager)GlobalConfiguration.Configuration.DependencyResolver.GetService(typeof(WebScriptHostManager));
            if (webScriptHostManager != null)
            {
                webScriptHostManager.Stop();
            }
        }
    }
}
