// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection
{
    /// <summary>
    /// An <see cref="IServiceProviderFactory{TContainerBuilder}"/> implementation that creates
    /// and populates an <see cref="JobHostServiceProvider"/> that can be used as the <see cref="IServiceProvider"/>
    /// </summary>
    public class JobHostScopedServiceProviderFactory : IServiceProviderFactory<IServiceCollection>
    {
        private readonly IServiceProvider _rootProvider;
        private readonly IServiceScopeFactory _rootScopeFactory;
        private readonly IDependencyValidator _validator;
        private JobHostServiceProvider _provider;

        public JobHostScopedServiceProviderFactory(IServiceProvider rootProvider, IServiceScopeFactory rootScopeFactory, IDependencyValidator validator)
        {
            _rootProvider = rootProvider ?? throw new ArgumentNullException(nameof(rootProvider));
            _rootScopeFactory = rootScopeFactory ?? throw new ArgumentNullException(nameof(rootScopeFactory));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        }

        public IServiceCollection CreateBuilder(IServiceCollection services)
        {
            return services;
        }

        public IServiceProvider CreateServiceProvider(IServiceCollection containerBuilder)
        {
            try
            {
                _validator.Validate(containerBuilder);
            }
            catch (InvalidHostServicesException ex)
            {
                // Log this to the WebHost's logger so we can track
                ILogger logger = _rootProvider.GetService<ILogger<DependencyValidator>>();
                logger.LogError(ex, "Invalid host services detected.");

                // rethrow to prevent host startup
                throw new HostInitializationException("Invalid host services detected.", ex);
            }

            if (_provider == null)
            {
                _provider = new JobHostServiceProvider(containerBuilder, _rootProvider, _rootScopeFactory);
            }

            return _provider;
        }
    }
}
