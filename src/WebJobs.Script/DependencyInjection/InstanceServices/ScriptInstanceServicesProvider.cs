// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Azure.WebJobs.Script.DependencyInjection
{
    internal class ScriptInstanceServicesProvider : IInstanceServicesProvider, IDisposable
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private IServiceScope _instanceServicesScope;
        private IServiceProvider _instanceServices;

        public ScriptInstanceServicesProvider(IServiceScopeFactory serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
        }

        public ScriptInstanceServicesProvider(IServiceProvider instanceServices)
        {
            _instanceServices = instanceServices;
        }

        public IServiceProvider InstanceServices
        {
            get
            {
                if (_instanceServices == null && _serviceScopeFactory != null)
                {
                    _instanceServicesScope = _serviceScopeFactory.CreateScope();
                    _instanceServices = _instanceServicesScope.ServiceProvider;
                }

                return _instanceServices;
            }

            set
            {
                _instanceServices = value;
            }
        }

        public void Dispose()
        {
            _instanceServicesScope?.Dispose();

            _instanceServicesScope = null;
            _instanceServices = null;
        }
    }
}
