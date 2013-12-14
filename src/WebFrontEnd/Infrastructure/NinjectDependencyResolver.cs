using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

using IMvcDependencyResolver = System.Web.Mvc.IDependencyResolver;
using IHttpDependencyResolver = System.Web.Http.Dependencies.IDependencyResolver;
using IHttpDependencyScope = System.Web.Http.Dependencies.IDependencyScope;
using Ninject;

namespace Microsoft.WindowsAzure.Jobs.Dashboard.Infrastructure
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
            return Kernel.TryGet(serviceType);
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
            return Kernel.TryGet(serviceType);
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