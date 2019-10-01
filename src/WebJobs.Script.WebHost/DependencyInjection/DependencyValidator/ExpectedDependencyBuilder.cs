// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection
{
    internal class ExpectedDependencyBuilder
    {
        private readonly IDictionary<Type, ServiceMatch> _serviceMatches = new Dictionary<Type, ServiceMatch>();

        private void AddNewMatch<T>(ServiceMatch match)
        {
            if (!_serviceMatches.TryAdd(typeof(T), match))
            {
                throw new InvalidOperationException($"Type {typeof(T)} has already been registered as expected.");
            }
        }

        public void Expect<TService, TImplementation>(ServiceLifetime lifetime = ServiceLifetime.Singleton)
        {
            ServiceMatch match = ServiceMatch.CreateMatch<TService>();
            AddNewMatch<TService>(match);
            match.Add<TImplementation>(lifetime);
        }

        public void Expect<TService>(string typeName, ServiceLifetime lifetime = ServiceLifetime.Singleton)
        {
            if (typeName == null)
            {
                throw new ArgumentNullException(nameof(typeName));
            }

            ServiceMatch match = ServiceMatch.CreateMatch<TService>();
            AddNewMatch<TService>(match);
            match.Add<TService>(typeName, lifetime);
        }

        public void ExpectFactory<TService>(ServiceLifetime lifetime = ServiceLifetime.Singleton)
        {
            ExpectFactory<TService, TService>(lifetime);
        }

        public void ExpectFactory<TService, TPeerType>(ServiceLifetime lifetime = ServiceLifetime.Singleton)
        {
            ServiceMatch match = ServiceMatch.CreateMatch<TService>();
            AddNewMatch<TService>(match);
            match.AddFactory<TPeerType>(lifetime);
        }

        public void Expect<TService, TPeerType>(string typeName, ServiceLifetime lifetime = ServiceLifetime.Singleton)
        {
            if (typeName == null)
            {
                throw new ArgumentNullException(nameof(typeName));
            }

            ServiceMatch match = ServiceMatch.CreateMatch<TService>();
            AddNewMatch<TService>(match);
            match.Add<TPeerType>(typeName, lifetime);
        }

        public void ExpectNone<TService>()
        {
            ServiceMatch match = ServiceMatch.CreateNoneMatch<TService>();
            AddNewMatch<TService>(match);
        }

        public void ExpectInstance<TService, TExpectedType>()
        {
            ServiceMatch match = ServiceMatch.CreateMatch<TService>();
            AddNewMatch<TService>(match);
            match.AddInstance<TExpectedType>();
        }

        public ExpectedCollectionBuilder ExpectCollection<TService>()
        {
            ServiceMatch match = ServiceMatch.CreateCollectionMatch<TService>();
            AddNewMatch<TService>(match);
            return new ExpectedCollectionBuilder(match);
        }

        public ExpectedCollectionBuilder ExpectSubcollection<TService>()
        {
            ServiceMatch match = ServiceMatch.CreateSubcollectionMatch<TService>();
            AddNewMatch<TService>(match);
            return new ExpectedCollectionBuilder(match);
        }

        public IEnumerable<InvalidServiceDescriptor> FindInvalidServices(IServiceCollection services)
        {
            List<InvalidServiceDescriptor> invalidDescriptors = new List<InvalidServiceDescriptor>();

            foreach (ServiceMatch match in _serviceMatches.Values)
            {
                invalidDescriptors.AddRange(match.FindInvalidServices(services));
            }

            return invalidDescriptors;
        }
    }
}
