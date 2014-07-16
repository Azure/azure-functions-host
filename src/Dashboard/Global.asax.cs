// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using Dashboard.App_Start;
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
                    // If we get here, it means that we had an indexing exception and
                    // error logging failed
                    HttpContext.Current.Items["IndexingException"] = exception.Message;
                }
            }
        }
    }
}
