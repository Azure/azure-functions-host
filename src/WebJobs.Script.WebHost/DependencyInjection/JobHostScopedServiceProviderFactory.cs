// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
        private readonly ILogger _logger;

        public JobHostScopedServiceProviderFactory(IServiceProvider rootProvider, IServiceCollection rootServices, IDependencyValidator validator)
        {
            _rootProvider = rootProvider ?? throw new ArgumentNullException(nameof(rootProvider));
            _rootServices = rootServices ?? throw new ArgumentNullException(nameof(rootServices));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _logger = ((ILogger)rootProvider.GetService<ILogger<JobHostScopedServiceProviderFactory>>()) ?? NullLogger.Instance;
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

            ShimBreakingChange(services);

            // Start from the root (web app level) as a base
            var jobHostServices = _rootProvider.CreateChildContainer(_rootServices);

            // ...and then add all the child services to this container
            foreach (var service in services)
            {
                jobHostServices.Add(service);
            }

            return jobHostServices.BuildServiceProvider();
        }

        /// <summary>
        /// .NET 8 has a breaking change regarding <see cref="ActivatorUtilitiesConstructorAttribute"/> no longer functioning as expected.
        /// We have some known extension types which are impacted by this. To avoid a regression, we are manually shimming those types.
        /// </summary>
        /// <param name="services">The service collection.</param>
        private void ShimBreakingChange(IServiceCollection services)
        {
            Dictionary<ServiceDescriptor, ServiceDescriptor> toReplace = null;
            static bool HasPreferredCtor(Type type)
            {
                foreach (ConstructorInfo c in type.GetConstructors())
                {
                    if (c.IsDefined(typeof(ActivatorUtilitiesConstructorAttribute), false))
                    {
                        return true;
                    }
                }

                return false;
            }

            void CreateReplacement(ServiceDescriptor descriptor)
            {
                if (!HasPreferredCtor(descriptor.ImplementationType))
                {
                    return;
                }

                _logger.LogInformation("Shimming DI constructor for {ImplementationType}.", descriptor.ImplementationType);
                toReplace ??= new Dictionary<ServiceDescriptor, ServiceDescriptor>();
                ObjectFactory factory = ActivatorUtilities.CreateFactory(descriptor.ImplementationType, Type.EmptyTypes);

                ServiceDescriptor replacement = ServiceDescriptor.Describe(
                    descriptor.ServiceType, sp => factory.Invoke(sp, Type.EmptyTypes), descriptor.Lifetime);
                toReplace.Add(descriptor, replacement);
            }

            // NetheriteProviderFactory uses ActivatorUtilitiesConstructorAttribute. We will replace this implementation with an explicit delegate.
            Type netheriteProviderFactory = Type.GetType("DurableTask.Netherite.AzureFunctions.NetheriteProviderFactory, DurableTask.Netherite.AzureFunctions, Version=1.0.0.0, Culture=neutral, PublicKeyToken=ef8c4135b1b4225a");
            foreach (ServiceDescriptor descriptor in services)
            {
                if (netheriteProviderFactory is not null && descriptor.ImplementationType == netheriteProviderFactory)
                {
                    CreateReplacement(descriptor);
                }
            }

            foreach ((ServiceDescriptor key, ServiceDescriptor value) in toReplace)
            {
                services.Remove(key);
                services.Add(value);
            }
        }
    }
}
