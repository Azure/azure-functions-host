// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Web.Http.Controllers;
using System.Web.Mvc;
using Ninject;
using IHttpDependencyResolver = System.Web.Http.Dependencies.IDependencyResolver;
using IHttpDependencyScope = System.Web.Http.Dependencies.IDependencyScope;
using IMvcDependencyResolver = System.Web.Mvc.IDependencyResolver;

namespace Dashboard.Infrastructure
{
    [SuppressMessage("Microsoft.Design", "CA1063:ImplementIDisposableCorrectly")]
    public class NinjectDependencyResolver : IMvcDependencyResolver, IHttpDependencyResolver, IHttpDependencyScope
    {
        public NinjectDependencyResolver(IKernel kernel)
        {
            Kernel = kernel;
        }

        public IKernel Kernel { get; private set; }

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
            if (typeof(IHttpController).IsAssignableFrom(serviceType))
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

        [SuppressMessage("Microsoft.Design", "CA1063:ImplementIDisposableCorrectly")]
        [SuppressMessage("Microsoft.Usage", "CA1816:CallGCSuppressFinalizeCorrectly")]
        void IDisposable.Dispose()
        {
            // From IDependencyScope, so no-op
        }
    }
}
