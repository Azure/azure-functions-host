using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using Microsoft.Web.Infrastructure.DynamicModuleHelper;
using Dashboard.App_Start;
using Dashboard.Infrastructure;
using Ninject;
using Ninject.Modules;
using Ninject.Web.Common;
using Ninject.Selection.Heuristics;
using Ninject.Planning.Directives;
using Ninject.Activation;

[assembly: WebActivatorEx.PreApplicationStartMethod(typeof(NinjectWebCommon), "Start")]
[assembly: WebActivatorEx.ApplicationShutdownMethodAttribute(typeof(NinjectWebCommon), "Stop")]

namespace Dashboard.App_Start
{
    public static class NinjectWebCommon
    {
        private static readonly Bootstrapper bootstrapper = new Bootstrapper();

        public static IKernel Kernel { get { return bootstrapper.Kernel; } }

        /// <summary>
        /// Starts the application
        /// </summary>
        public static void Start()
        {
            // Registration must occur before application start. So use: WebActivator.PreApplicationStartMethod 
            DynamicModuleUtility.RegisterModule(typeof(OnePerRequestHttpModule));
            DynamicModuleUtility.RegisterModule(typeof(NinjectHttpModule));
            bootstrapper.Initialize(CreateKernel);

            // Set both MVC and WebAPI resolvers
            var resolver = new NinjectDependencyResolver(bootstrapper.Kernel);
            DependencyResolver.SetResolver(resolver);
            GlobalConfiguration.Configuration.DependencyResolver = resolver;
        }

        /// <summary>
        /// Stops the application.
        /// </summary>
        public static void Stop()
        {
            bootstrapper.ShutDown();
        }
        /// <summary>
        /// Creates the kernel that will manage your application.
        /// </summary>
        /// <returns>The created kernel.</returns>
        private static IKernel CreateKernel()
        {
            var kernel = new StandardKernel(GetModules().ToArray());
            try
            {
                kernel.Settings.InjectNonPublic = true;
                kernel.Bind<Func<IKernel>>().ToMethod(ctx => () => new Bootstrapper().Kernel);
                kernel.Bind<IHttpModule>().To<HttpApplicationInitializationHttpModule>();
                return kernel;
            }
            catch
            {
                kernel.Dispose();
                throw;
            }
        }

        private static IEnumerable<NinjectModule> GetModules()
        {
            yield return new AppModule();
        }
    }
}
