// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using Autofac;
using Autofac.Integration.Mvc;
using Autofac.Integration.WebApi;
using Dashboard.Data;
using Dashboard.Indexers;

namespace Dashboard
{
    public class MvcApplication : HttpApplication
    {
        private static IIndexer _indexer;

        // Include all both internal and public ctors.
        private static ConstructorInfo[] AllConstructors(Type type)
        {
            return type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }

        public static IContainer BuildContainer(HttpConfiguration config)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }
            ContainerBuilder builder = new ContainerBuilder();
            AppModule.Load(builder);

            // WebAPI has internal constructors. 
            builder.RegisterApiControllers(typeof(WebApiConfig).Assembly).FindConstructorsWith(AllConstructors);
            builder.RegisterControllers(typeof(WebApiConfig).Assembly).FindConstructorsWith(AllConstructors);

            var container = builder.Build();

            // Set MVC and WebApi resolved to AutoFac. 
            DependencyResolver.SetResolver(new AutofacDependencyResolver(container));
                        
            config.DependencyResolver = new AutofacWebApiDependencyResolver(container);

            return container;
        }

        protected void Application_Start()
        {
            var container = BuildContainer(GlobalConfiguration.Configuration);

            GlobalConfiguration.Configure(WebApiConfig.Register);
            AreaRegistration.RegisterAllAreas();

            var context = container.Resolve<DashboardAccountContext>();

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
                HostVersionConfig.RegisterWarnings(container.Resolve<IHostVersionReader>());
            }

            _indexer = container.ResolveOptional<IIndexer>();

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
