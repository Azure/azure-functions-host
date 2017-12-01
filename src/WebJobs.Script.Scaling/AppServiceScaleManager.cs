// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Scaling
{
    public sealed class AppServiceScaleManager : ScaleManager
    {
        private static AppServiceScaleManager _instance;

        private AppServiceScaleManager(IWorkerInfoProvider provider)
            : base(provider, AppServiceWorkerTable.Instance, AppServiceScaleHandler.Instance, AppServiceEventSource.Instance, ScaleSettings.Instance)
        {
        }

        public static bool Enabled
        {
            get
            {
                return AppServiceSettings.RuntimeScalingEnabled.Value &&
                    !string.IsNullOrEmpty(AppServiceSettings.StorageConnectionString) &&
                    string.Equals("Dynamic", AppServiceSettings.Sku, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        ///  this is the main entry point for function runtime
        /// </summary>
        public static void RegisterProvider(IWorkerStatusProvider provider)
        {
            _instance?.Dispose();
            _instance = null;

            if (!Enabled)
            {
                AppServiceEventSource.Instance.Warning(AppServiceSettings.SiteName, AppServiceSettings.CurrentStampName, AppServiceSettings.WorkerName, "Runtime scaling is not enabled.");

                return;
            }

            if (provider != null)
            {
                AppServiceScaleManager instance = null;
                try
                {
                    instance = new AppServiceScaleManager(new AppServiceWorkerInfoProvider(provider));
                    instance.Start();

                    // assign
                    _instance = instance;
                    instance = null;
                }
                finally
                {
                    instance?.Dispose();
                }
            }

            AppServiceEventSource.Instance.Information(AppServiceSettings.SiteName, AppServiceSettings.CurrentStampName, AppServiceSettings.WorkerName, string.Format("Worker status provider is {0}", provider != null ? "registered" : "unregistered"));
        }
    }
}