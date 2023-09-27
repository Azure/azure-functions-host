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

        public IServiceCollection CreateBuilder(IServiceCollection services)
        {
            return services;
        }

        /// <summary>
        /// This creates the service provider *and the end* of spinning up the JobHost.
        /// When we build the ScriptHost (<see cref="DefaultScriptHostBuilder.BuildHost(bool, bool)"/>),
        /// all services are fed in here (<paramref name="services"/>), and using that list we build
        /// a provider that has all of the base level services that we want to copy, then adds all of the
        /// SciptHost level services on top. It is not a proxying provider, we are copying the services
        /// references into that (rarely - e.g. startup and specialization) created ScriptHost layer scope.
        /// </summary>
        /// <param name="services">The ScriptHost services to add on top of the copied root services.</param>
        /// <returns>A provider containing the superset of base (application) level services and ScriptHost servics.</returns>
        /// <exception cref="HostInitializationException">If service validation fails (e.g. user touches something they shouldn't have.</exception>
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
