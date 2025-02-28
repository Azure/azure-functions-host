// These extensions are based on Orchard (https://github.com/OrchardCMS/OrchardCore)
// BSD 3 - Clause License
//  https://opensource.org/licenses/BSD-3-Clause
//
// Copyright(c).NET Foundation. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection
{
    public static class ServiceProviderExtensions
    {
        /// <summary>
        /// Things we don't want to copy down to child containers because...
        /// </summary>
        private static readonly HashSet<Type> ChildContainerIgnoredTypes = new()
        {
            typeof(IStartupFilter),        // This would re-add middlewares to the host pipeline
            typeof(IManagedHostedService), // These shouldn't be instantiated twice
            typeof(IHostedService),        // These shouldn't be instantiated twice
            typeof(ILoggerProvider),        // These shouldn't be instantiated twice
        };

        /// <summary>
        /// Creates a child container.
        /// </summary>
        /// <param name="serviceProvider">The service provider to create a child container for.</param>
        /// <param name="serviceCollection">The services to clone.</param>
        public static IServiceCollection CreateChildContainer(this IServiceProvider serviceProvider, IServiceCollection serviceCollection)
        {
            IServiceCollection clonedCollection = new ServiceCollection();
            var servicesByType = serviceCollection.GroupBy(s => s.ServiceType);

            foreach (var services in servicesByType)
            {
                if (ChildContainerIgnoredTypes.Contains(services.Key))
                {
                    // Do nothing - we don't want to copy these to a child scope for reasons above
                }

                // A generic type definition is rather used to create other constructed generic types.
                else if (services.Key.IsGenericTypeDefinition)
                {
                    // So, we just need to pass the descriptor.
                    foreach (var service in services)
                    {
                        clonedCollection.Add(service);
                    }
                }

                // If only one service of a given type.
                else if (services.Count() == 1)
                {
                    var service = services.First();

                    if (service.Lifetime == ServiceLifetime.Singleton)
                    {
                        // An host singleton is shared across tenant containers but only registered instances are not disposed
                        // by the DI, so we check if it is disposable or if it uses a factory which may return a different type.

                        if (typeof(IDisposable).IsAssignableFrom(service.GetImplementationType()) || service.ImplementationFactory != null)
                        {
                            // If disposable, register an instance that we resolve immediately from the main container.
                            clonedCollection.CloneSingleton(service, serviceProvider.GetService(service.ServiceType));
                        }
                        else
                        {
                            // If not disposable, the singleton can be resolved through a factory when first requested.
                            clonedCollection.CloneSingleton(service, _ => serviceProvider.GetService(service.ServiceType));

                            // Note: Most of the time a singleton of a given type is unique and not disposable. So,
                            // most of the time it will be resolved when first requested through a tenant container.
                        }
                    }
                    else
                    {
                        clonedCollection.Add(service);
                    }
                }

                // If all services of the same type are not singletons.
                else if (services.All(s => s.Lifetime != ServiceLifetime.Singleton))
                {
                    // We don't need to resolve them.
                    foreach (var service in services)
                    {
                        clonedCollection.Add(service);
                    }
                }

                // If all services of the same type are singletons.
                else if (services.All(s => s.Lifetime == ServiceLifetime.Singleton))
                {
                    // We can resolve them from the main container.
                    // Materialize services and instances to arrays once.
                    var servicesArray = services.ToArray();
                    var instancesArray = serviceProvider.GetServices(services.Key)?.ToArray() ?? Array.Empty<object>();

                    // Validate counts
                    ValidateServiceAndInstanceCounts(servicesArray, instancesArray, services.Key);

                    for (var i = 0; i < servicesArray.Length; i++)
                    {
                        clonedCollection.CloneSingleton(servicesArray[i], instancesArray[i]);
                    }
                }

                // If singletons and scoped services are mixed.
                else
                {
                    // We need a service scope to resolve them.
                    using var scope = serviceProvider.CreateScope();

                    // Materialize services and instances to arrays once.
                    var servicesArray = services.ToArray();
                    var instancesArray = scope.ServiceProvider.GetServices(services.Key)?.ToArray() ?? Array.Empty<object>();

                    ValidateServiceAndInstanceCounts(servicesArray, instancesArray, services.Key);

                    // Then we only keep singleton instances.
                    for (var i = 0; i < servicesArray.Length; i++)
                    {
                        if (servicesArray[i].Lifetime == ServiceLifetime.Singleton)
                        {
                            clonedCollection.CloneSingleton(servicesArray[i], instancesArray[i]);
                        }
                        else
                        {
                            clonedCollection.Add(servicesArray[i]);
                        }
                    }
                }
            }

            return clonedCollection;
        }

        private static void ValidateServiceAndInstanceCounts<TService, TInstance>(TService[] servicesArray, TInstance[] instancesArray, Type serviceType)
        {
            if (servicesArray.Length != instancesArray.Length)
            {
                var message = $"""
                                Mismatch detected for type {serviceType}:
                                services.Count = {servicesArray.Length}, instances.Count = {instancesArray.Length}
                                Services:
                                  {string.Join(",\n\t", servicesArray.Select(s => s.ToString()))}
                                Instances:
                                  {string.Join(",\n\t", instancesArray.Select(i => i?.GetType()?.ToString() ?? "null"))}
                                """;

                throw new InvalidOperationException(message);
            }
        }
    }

    internal static class ServiceCollectionExtensions
    {
        public static IServiceCollection CloneSingleton(
            this IServiceCollection services,
            ServiceDescriptor parent,
            object implementationInstance)
        {
            // If the instance is null, then only a lambda result is valid
            var cloned = implementationInstance == null
                ? new ClonedSingletonDescriptor(parent, _ => null)
                : new ClonedSingletonDescriptor(parent, implementationInstance);
            services.Add(cloned);
            return services;
        }

        public static IServiceCollection CloneSingleton(
            this IServiceCollection collection,
            ServiceDescriptor parent,
            Func<IServiceProvider, object> implementationFactory)
        {
            var cloned = new ClonedSingletonDescriptor(parent, implementationFactory);
            collection.Add(cloned);
            return collection;
        }
    }

    public class ClonedSingletonDescriptor : ServiceDescriptor
    {
        public ClonedSingletonDescriptor(ServiceDescriptor parent, object implementationInstance)
            : base(parent.ServiceType, implementationInstance)
        {
            Parent = parent;
        }

        public ClonedSingletonDescriptor(ServiceDescriptor parent, Func<IServiceProvider, object> implementationFactory)
            : base(parent.ServiceType, implementationFactory, ServiceLifetime.Singleton)
        {
            Parent = parent;
        }

        public ServiceDescriptor Parent { get; }
    }

    public static class ServiceDescriptorExtensions
    {
        public static Type GetImplementationType(this ServiceDescriptor descriptor)
        {
            if (descriptor is ClonedSingletonDescriptor cloned)
            {
                // Use the parent descriptor as it was before being cloned.
                return cloned.Parent.GetImplementationType();
            }

            if (descriptor.ImplementationType != null)
            {
                return descriptor.ImplementationType;
            }

            if (descriptor.ImplementationInstance != null)
            {
                return descriptor.ImplementationInstance.GetType();
            }

            if (descriptor.ImplementationFactory != null)
            {
                return descriptor.ImplementationFactory.GetType().GenericTypeArguments[1];
            }

            return null;
        }
    }
}