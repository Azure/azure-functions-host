using System;
using System.Collections.Generic;
using System.Web.Http.Controllers;
using System.Web.Mvc;
using Ninject;
using IHttpDependencyResolver = System.Web.Http.Dependencies.IDependencyResolver;
using IHttpDependencyScope = System.Web.Http.Dependencies.IDependencyScope;
using IMvcDependencyResolver = System.Web.Mvc.IDependencyResolver;

namespace Dashboard.Infrastructure
{
    public class NinjectDependencyResolver : IMvcDependencyResolver, IHttpDependencyResolver, IHttpDependencyScope
    {
        public IKernel Kernel { get; private set; }

        public NinjectDependencyResolver(IKernel kernel)
        {
            Kernel = kernel;
        }

        object IMvcDependencyResolver.GetService(Type serviceType)
        {
            if (typeof(IController).IsAssignableFrom(serviceType))
            {
                return Kernel.Get(serviceType);
            }
            else
            {
                return null;
            }
        }

        IEnumerable<object> IMvcDependencyResolver.GetServices(Type serviceType)
        {
            return Kernel.GetAll(serviceType);
        }

        IHttpDependencyScope IHttpDependencyResolver.BeginScope()
        {
            return this;
        }

        object IHttpDependencyScope.GetService(Type serviceType)
        {
            if (typeof (IHttpController).IsAssignableFrom(serviceType))
            {
                return Kernel.Get(serviceType);
            }
            else
            {
                return null;
            }
        }

        IEnumerable<object> IHttpDependencyScope.GetServices(Type serviceType)
        {
            return Kernel.GetAll(serviceType);
        }

        void IDisposable.Dispose()
        {
            // From IDependencyScope, so no-op
        }
    }
}
