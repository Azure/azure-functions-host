// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Threading;
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

            if (settingsManager.IsDynamicSku)
            {
                ServicePointManager.DefaultConnectionLimit = ScriptConstants.DynamicSkuConnectionLimit;
            }

            ConfigureMinimumThreads(settingsManager.IsDynamicSku);
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

        private static void ConfigureMinimumThreads(bool isDynamicSku)
        {
            // For information on MinThreads, see:
            // https://docs.microsoft.com/en-us/dotnet/api/system.threading.threadpool.setminthreads?view=netframework-4.7
            // https://docs.microsoft.com/en-us/azure/redis-cache/cache-faq#important-details-about-threadpool-growth
            // https://blogs.msdn.microsoft.com/perfworld/2010/01/13/how-can-i-improve-the-performance-of-asp-net-by-adjusting-the-clr-thread-throttling-properties/
            //
            // This behavior can be overridden by using the "ComPlus_ThreadPool_ForceMinWorkerThreads" environment variable (honored by the .NET threadpool).

            // The dynamic plan has some limits that mean that a given instance is using effectively a single core, so we should not use Environment.Processor count in this case.
            var effectiveCores = isDynamicSku ? 1 : Environment.ProcessorCount;

            // This value was derived by looking at the thread count for several function apps running load on a multicore machine and dividing by the number of cores.
            const int minThreadsPerLogicalProcessor = 6;

            int minThreadCount = effectiveCores * minThreadsPerLogicalProcessor;
            ThreadPool.SetMinThreads(minThreadCount, minThreadCount);
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
