// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using DryIoc;

namespace Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection
{
    public class ScopedServiceProvider : IServiceProvider, IDisposable
    {
        private readonly IResolverContext _resolver;

        public ScopedServiceProvider(IResolverContext container)
        {
            _resolver = container;
        }

        public void Dispose()
        {
            _resolver.Dispose();
        }

        public object GetService(Type serviceType)
        {
            return _resolver.Resolve(serviceType, IfUnresolved.ReturnDefault);
        }
    }
}
