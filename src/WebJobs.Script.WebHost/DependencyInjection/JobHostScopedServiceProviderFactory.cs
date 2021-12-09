// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection
{
    /// <summary>
    /// An <see cref="IServiceProviderFactory{TContainerBuilder}"/> implementation that creates
    /// and populates a child scope that can be used as the <see cref="IServiceProvider"/>.
    /// </summary>
    public class JobHostScopedServiceProviderFactory : IServiceProviderFactory<IServiceCollection>
    {
        private readonly IServiceProvider _rootProvider;
        private readonly IServiceCollection _rootServices;
        private readonly IDependencyValidator _validator;

        public JobHostScopedServiceProviderFactory(IServiceProvider rootProvider, IServiceCollection rootServices, IDependencyValidator validator)
        {
            _rootProvider = rootProvider ?? throw new ArgumentNullException(nameof(rootProvider));
            _rootServices = rootServices ?? throw new ArgumentNullException(nameof(rootServices));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        }

        public IServiceCollection CreateBuilder(IServiceCollection services) => services;

        public IServiceProvider CreateServiceProvider(IServiceCollection services)
        {
            try
            {
                // Validating that a customer hasn't overridden things that they shouldn't
                _validator.Validate(services);
            }
            catch (InvalidHostServicesException ex)
            {
                // Log this to the WebHost's logger so we can track
                ILogger logger = _rootProvider.GetService<ILogger<DependencyValidator>>();
                logger.LogError(ex, "Invalid host services detected.");

                // rethrow to prevent host startup
                throw new HostInitializationException("Invalid host services detected.", ex);
            }

            // Start from the root (web app level) as a base
            var jobHostServices = _rootProvider.CreateChildContainer(_rootServices);

            // ...and then add all the child services to this container
            foreach (var service in services)
            {
                jobHostServices.Add(service);
            }

            return jobHostServices.BuildServiceProvider();
        }
    }
}
