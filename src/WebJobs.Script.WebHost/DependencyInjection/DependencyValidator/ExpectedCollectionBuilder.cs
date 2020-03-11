// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection
{
    internal class ExpectedCollectionBuilder
    {
        private readonly ServiceMatch _match;

        public ExpectedCollectionBuilder(ServiceMatch match)
        {
            _match = match;
        }

        public ExpectedCollectionBuilder Expect<TImplementation>(ServiceLifetime lifetime = ServiceLifetime.Singleton)
        {
            _match.Add<TImplementation>(lifetime);
            return this;
        }

        public ExpectedCollectionBuilder Expect(string typeName, ServiceLifetime lifetime = ServiceLifetime.Singleton)
        {
            _match.Add(typeName, lifetime);
            return this;
        }

        public ExpectedCollectionBuilder Expect<TPeerType>(string typeName, ServiceLifetime lifetime = ServiceLifetime.Singleton)
        {
            _match.Add<TPeerType>(typeName, lifetime);
            return this;
        }

        public ExpectedCollectionBuilder ExpectFactory<TFactoryType>(ServiceLifetime lifetime = ServiceLifetime.Singleton)
        {
            _match.AddFactory<TFactoryType>(lifetime);
            return this;
        }

        public ExpectedCollectionBuilder Optional<TImplementation>(ServiceLifetime lifetime = ServiceLifetime.Singleton)
        {
            _match.AddOptional<TImplementation>(lifetime);
            return this;
        }

        public ExpectedCollectionBuilder OptionalExternal(string externalTypeName, string assemblyName, string assemblyPublicKeyToken)
        {
            _match.AddOptionalExternal(externalTypeName, assemblyName, assemblyPublicKeyToken);
            return this;
        }
    }
}
