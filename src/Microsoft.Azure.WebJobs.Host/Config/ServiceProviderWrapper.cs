// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Host.Config
{    
    // Used to wrap a JobHostConfiguration. Let's JobHost startup modify services  
    // without modifying the underlying configuration. 
    internal class ServiceProviderWrapper : IServiceProvider
    {
        private readonly IServiceProvider _inner;

        private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

        public ServiceProviderWrapper(IServiceProvider inner)
        {
            _inner = inner;
        }

        public void AddService<T>(T service)
        {
            _services[typeof(T)] = service;
        }

        public object GetService(Type serviceType)
        {
            object service;
            if (!_services.TryGetValue(serviceType, out service))
            {
                service = _inner.GetService(serviceType);
            }
            return service;
        }
    }
}
