// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Web.Http;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class WebApiApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            GlobalConfiguration.Configure(WebApiConfig.Register);
        }

        protected void Application_Error(object sender, EventArgs e)
        {
            // TODO: Log any unhandled exceptions
            Exception ex = Server.GetLastError();
        }

        protected void Application_End(object sender, EventArgs e)
        {
            WebScriptHostManager webScriptHostManager = (WebScriptHostManager)GlobalConfiguration.Configuration.DependencyResolver.GetService(typeof(WebScriptHostManager));
            if (webScriptHostManager != null)
            {
                webScriptHostManager.Stop();
            }
        }
    }
}
