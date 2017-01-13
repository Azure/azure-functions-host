// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class WebApiApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            var settingsManager = ScriptSettingsManager.Instance;
            var webHostSettings = WebHostSettings.CreateDefault(settingsManager);

            VerifyAndEnableShadowCopy(webHostSettings);

            using (var metricsLogger = new WebHostMetricsLogger())
            using (metricsLogger.LatencyEvent(MetricEventNames.ApplicationStartLatency))
            {
                GlobalConfiguration.Configure(c => WebApiConfig.Initialize(c, settingsManager, webHostSettings));
            }
        }

        private static void VerifyAndEnableShadowCopy(WebHostSettings webHostSettings)
        {
            if (!FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagDisableShadowCopy))
            {
                string currentShadowCopyDirectories = AppDomain.CurrentDomain.SetupInformation.ShadowCopyDirectories;
                string shadowCopyPath = GetShadowCopyPath(currentShadowCopyDirectories, webHostSettings.ScriptPath);

#pragma warning disable CS0618
                AppDomain.CurrentDomain.SetShadowCopyPath(shadowCopyPath);
#pragma warning restore CS0618
            }
        }

        internal static string GetShadowCopyPath(string currentShadowCopyDirectories, string scriptPath)
        {
            return string.Join(";", new[] { currentShadowCopyDirectories, scriptPath }.Where(s => !string.IsNullOrEmpty(s)));
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
