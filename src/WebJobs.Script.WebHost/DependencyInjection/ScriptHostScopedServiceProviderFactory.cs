// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection
{
    /// <summary>
    /// An <see cref="IServiceProviderFactory{TContainerBuilder}"/> implementation that creates
    /// and populates an <see cref="ScriptHostServiceProvider"/> that can be used as the <see cref="IServiceProvider"/>
    /// </summary>
    public class ScriptHostScopedServiceProviderFactory : IServiceProviderFactory<IServiceCollection>
    {
        private readonly IServiceProvider _rootProvider;
        private readonly IServiceScopeFactory _rootScopeFactory;
        private ScriptHostServiceProvider _provider;

        public ScriptHostScopedServiceProviderFactory(IServiceProvider rootProvider, IServiceScopeFactory rootScopeFactory)
        {
            _rootProvider = rootProvider ?? throw new ArgumentNullException(nameof(rootProvider));
            _rootScopeFactory = rootScopeFactory ?? throw new ArgumentNullException(nameof(rootScopeFactory));
        }

        public IServiceCollection CreateBuilder(IServiceCollection services)
        {
            return services;
        }

        public IServiceProvider CreateServiceProvider(IServiceCollection containerBuilder)
        {
            if (_provider == null)
            {
                _provider = new ScriptHostServiceProvider(containerBuilder, _rootProvider, _rootScopeFactory);
            }

            return _provider;
        }
    }
}
