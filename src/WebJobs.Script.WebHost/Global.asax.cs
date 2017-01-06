// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Web.Http;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class WebApiApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            using (var metricsLogger = new WebHostMetricsLogger())
            using (metricsLogger.LatencyEvent(MetricEventNames.ApplicationStartLatency))
            {
                GlobalConfiguration.Configure(c => WebApiConfig.Initialize(c));
            }
        }

        protected void Application_Error(object sender, EventArgs e)
        {
            // TODO: Log any unhandled exceptions
        }

        protected void Application_End(object sender, EventArgs e)
        {
            WebScriptHostManager webScriptHostManager = GlobalConfiguration.Configuration.DependencyResolver.GetService<WebScriptHostManager>();
            if (webScriptHostManager != null)
            {
                webScriptHostManager.Stop();
                webScriptHostManager.Dispose();
            }
        }
    }
}
