// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection
{
    internal class ServiceMatch
    {
        private readonly ICollection<ServiceDescriptor> _requiredDescriptors = new Collection<ServiceDescriptor>();
        private readonly ICollection<ServiceDescriptor> _optionalDescriptors = new Collection<ServiceDescriptor>();
        private readonly MatchType _match;

        private ServiceMatch(Type serviceType, MatchType match)
        {
            ServiceType = serviceType;
            _match = match;
        }

        private enum MatchType
        {
            None,
            Single,
            Collection,
            Subcollection
        }

        public Type ServiceType { get; private set; }

        /// <summary>
        /// Indicates that the expected service must not have a registered ServiceDescriptor.
        /// </summary>
        public static ServiceMatch CreateNoneMatch<T>()
        {
            return new ServiceMatch(typeof(T), MatchType.None);
        }

        /// <summary>
        /// Indicates that the expected service must be the last one registered for this ServiceType.
        /// </summary>
        public static ServiceMatch CreateMatch<T>()
        {
            return new ServiceMatch(typeof(T), MatchType.Single);
        }

        /// <summary>
        /// Indicates that the expected services must match exactly the collection of services registered for this ServiceType.
        /// </summary>
        public static ServiceMatch CreateCollectionMatch<T>()
        {
            return new ServiceMatch(typeof(T), MatchType.Collection);
        }

        /// <summary>
        /// Indicates that the expected service must exist in a collection of services registered for this ServiceType.
        /// </summary>
        public static ServiceMatch CreateSubcollectionMatch<T>()
        {
            return new ServiceMatch(typeof(T), MatchType.Subcollection);
        }

        public void Add<TImplementation>(ServiceLifetime lifetime)
        {
            ServiceDescriptor desc = new ServiceDescriptor(ServiceType, typeof(TImplementation), lifetime);
            Add(desc);
        }

        public void Add<TPeerType>(string typeName, ServiceLifetime lifetime)
        {
            if (typeName == null)
            {
                throw new ArgumentNullException(nameof(typeName));
            }

            Type peerType = typeof(TPeerType);
            Add(peerType, typeName, lifetime);
        }

        public void Add(string typeName, ServiceLifetime lifetime)
        {
            if (typeName == null)
            {
                throw new ArgumentNullException(nameof(typeName));
            }

            Add(ServiceType, typeName, lifetime);
        }

        private void Add(Type peerType, string typeName, ServiceLifetime lifetime)
        {
            // So we don't have to build the full type each time we rev an assembly,
            // use a peer type to construct the full name
            string fullTypeName = peerType.AssemblyQualifiedName.Replace(peerType.FullName, typeName);

            ServiceDescriptor desc = new ServiceDescriptor(ServiceType, Type.GetType(fullTypeName), lifetime);
            Add(desc);
        }

        public void AddFactory<TFactoryType>(ServiceLifetime lifetime)
        {
            // Hijack the factory to pass in the expected type
            ServiceDescriptor desc = new ServiceDescriptor(ServiceType, s => typeof(TFactoryType).Assembly, lifetime);
            Add(desc);
        }

        public void AddInstance<TInstanceType>()
        {
            // Hijack the instance property pass in the expected type
            ServiceDescriptor desc = new ServiceDescriptor(ServiceType, typeof(TInstanceType));
            Add(desc);
        }

        private void Add(ServiceDescriptor desc)
        {
            if (_match == MatchType.Single && _requiredDescriptors.Any())
            {
                throw new InvalidOperationException($"{nameof(MatchType)} is {nameof(MatchType.Single)} and there is already an expected descriptor.");
            }

            _requiredDescriptors.Add(desc);
        }

        public void AddOptional<TImplementation>(ServiceLifetime lifetime)
        {
            if (_match != MatchType.Collection)
            {
                throw new InvalidOperationException("Optional matches only apply to Collections.");
            }

            ServiceDescriptor desc = new ServiceDescriptor(ServiceType, typeof(TImplementation), lifetime);
            _optionalDescriptors.Add(desc);
        }

        public IEnumerable<InvalidServiceDescriptor> FindInvalidServices(IServiceCollection services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            IEnumerable<ServiceDescriptor> registered = services.Where(p => p.ServiceType == ServiceType);

            switch (_match)
            {
                case MatchType.None:
                    // If we expect none, all of the registered services for this type are invalid
                    return registered.Select(p => new InvalidServiceDescriptor(p, InvalidServiceDescriptorReason.Invalid));

                case MatchType.Single:
                    ServiceDescriptor expected = _requiredDescriptors.Single();
                    ServiceDescriptor lastMatch = registered.LastOrDefault();

                    if (!IsMatch(expected, lastMatch))
                    {
                        return new[] { new InvalidServiceDescriptor(lastMatch, InvalidServiceDescriptorReason.Invalid) };
                    }

                    return Enumerable.Empty<InvalidServiceDescriptor>();

                case MatchType.Collection:
                    return FindCollectionDifferences(registered, _requiredDescriptors, _optionalDescriptors);

                case MatchType.Subcollection:
                    return FindMissingServicesInCollection(registered, _requiredDescriptors);

                default:
                    throw new InvalidOperationException($"Invalid value: '{_match}'");
            }
        }

        private static bool IsMatch(ServiceDescriptor expected, ServiceDescriptor registered)
        {
            bool factoryMatches = expected.ImplementationFactory == registered.ImplementationFactory;
            bool instanceMatches = expected.ImplementationInstance == registered.ImplementationInstance;

            if (expected.ImplementationFactory != null && registered.ImplementationFactory != null)
            {
                // Make sure the factory is declared in the assembly mentioned; that's enough
                // to make sure that it's not been overridden. Use the hijacked factory from
                // when we registered this descriptor to pull out the expected Assembly.
                Assembly expectedAssembly = (Assembly)expected.ImplementationFactory(null);
                factoryMatches = expectedAssembly == registered.ImplementationFactory.GetMethodInfo().DeclaringType.Assembly;
            }
            else if (expected.ImplementationInstance != null && registered.ImplementationInstance != null)
            {
                // A non-null ImplementationInstance signals we expect this to be an Instance, and we've
                // stored the type we expect in the expected ServiceDescriptor.
                Type expectedInstanceType = expected.ImplementationInstance as Type;
                instanceMatches = expectedInstanceType == registered.ImplementationInstance.GetType();
            }

            return factoryMatches &&
                instanceMatches &&
                expected.ImplementationType == registered.ImplementationType &&
                expected.Lifetime == registered.Lifetime &&
                expected.ServiceType == registered.ServiceType;
        }

        private static IEnumerable<InvalidServiceDescriptor> FindCollectionDifferences(IEnumerable<ServiceDescriptor> registeredServices, IEnumerable<ServiceDescriptor> expectedServices, IEnumerable<ServiceDescriptor> optionalServices)
        {
            // Don't report optional services as missing.
            var missingServices = FindMissingServicesInCollection(registeredServices, expectedServices);

            var extraServices = registeredServices
                .Where(r => !expectedServices.Any(e => IsMatch(e, r)) && !optionalServices.Any(o => IsMatch(o, r)))
                .Select(p => new InvalidServiceDescriptor(p, InvalidServiceDescriptorReason.Invalid));

            return missingServices.Concat(extraServices);
        }

        private static IEnumerable<InvalidServiceDescriptor> FindMissingServicesInCollection(IEnumerable<ServiceDescriptor> registeredServices, IEnumerable<ServiceDescriptor> expectedServices)
        {
            return expectedServices
                .Where(e => !registeredServices.Any(r => IsMatch(e, r)))
                .Select(p => new InvalidServiceDescriptor(p, InvalidServiceDescriptorReason.Missing));
        }
    }
}
