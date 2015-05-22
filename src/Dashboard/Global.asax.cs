// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using Dashboard.AppStart;
using Dashboard.Data;
using Dashboard.Indexers;
using Ninject;

namespace Dashboard
{
    public class MvcApplication : HttpApplication
    {
        private static IIndexer _indexer;

        protected void Application_Start()
        {
            var kernel = NinjectWebCommon.Kernel;

            DashboardAccountContext context = kernel.Get<DashboardAccountContext>();

            AreaRegistration.RegisterAllAreas();

            GlobalConfiguration.Configure(WebApiConfig.Register);
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);

            if (!context.HasSetupError)
            {
                RouteConfig.RegisterRoutes(RouteTable.Routes);
            }
            else 
            { 
                RouteConfig.RegisterNoAccountRoutes(RouteTable.Routes); 
            }
            
            BundleConfig.RegisterBundles(BundleTable.Bundles);

            if (!context.HasSetupError)
            {
                ModelBinderConfig.Register();
                HostVersionConfig.RegisterWarnings(kernel.Get<IHostVersionReader>());
            }

            _indexer = kernel.TryGet<IIndexer>();

            if (_indexer != null)
            {
                // Using private threads for now. If indexing switches to async storage calls we need to
                // either use the CLR threadpool or figure out how to schedule the async callbacks on the
                // private threads.
                for (int i = 0; i < Environment.ProcessorCount; i++)
                {
                    new Thread(IndexerWorkerLoop).Start();
                }
            }
        }

        private void IndexerWorkerLoop()
        {
            const int IndexerPollIntervalMilliseconds = 5000;

            while (true)
            {
                try
                {
                    _indexer.Update();
                }
                catch (Exception) 
                {
                    // Swallow any exceptions from the background thread to avoid killing the worker
                    // process. We should only get here if logging failed for indexer exceptions.
                }

                Thread.Sleep(IndexerPollIntervalMilliseconds);
            }
        }
    }
}
