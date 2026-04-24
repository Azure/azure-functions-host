// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using Azure.Core;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// A decorator around <see cref="AzureComponentFactory"/> that adds support for the "Broker" credential type.
    /// When a configuration section has <c>credential=Broker</c>, this factory creates a <see cref="BrokerTokenCredential"/>
    /// that acquires storage tokens from a token broker sidecar. For all other credential types, it delegates
    /// to the inner factory.
    /// </summary>
    internal sealed class BrokerAwareComponentFactory : AzureComponentFactory
    {
        private const string CredentialKey = "credential";
        private const string BrokerCredentialValue = "Broker";
        private const string BrokerEndpointKey = "brokerEndpoint";
        private const string BrokerApiKeyKey = "brokerApiKey";

        private readonly AzureComponentFactory _inner;
        private readonly object _credentialLock = new object();
        private BrokerTokenCredential _cachedCredential;

        public BrokerAwareComponentFactory(AzureComponentFactory inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <inheritdoc/>
        public override TokenCredential CreateTokenCredential(IConfiguration configuration)
        {
            var credentialType = configuration[CredentialKey];

            if (string.Equals(credentialType, BrokerCredentialValue, StringComparison.OrdinalIgnoreCase))
            {
                var brokerEndpoint = configuration[BrokerEndpointKey];
                var brokerApiKey = configuration[BrokerApiKeyKey];

                if (string.IsNullOrEmpty(brokerEndpoint))
                {
                    throw new InvalidOperationException(
                        $"Storage credential type is '{BrokerCredentialValue}' but '{BrokerEndpointKey}' is not configured.");
                }

                if (string.IsNullOrEmpty(brokerApiKey))
                {
                    throw new InvalidOperationException(
                        $"Storage credential type is '{BrokerCredentialValue}' but '{BrokerApiKeyKey}' is not configured.");
                }

                return GetOrCreateBrokerCredential(brokerEndpoint, brokerApiKey);
            }

            return _inner.CreateTokenCredential(configuration);
        }

        /// <inheritdoc/>
        public override object CreateClientOptions(Type optionsType, object serviceVersion, IConfiguration configuration)
        {
            return _inner.CreateClientOptions(optionsType, serviceVersion, configuration);
        }

        /// <inheritdoc/>
        public override object CreateClient(Type clientType, IConfiguration configuration, TokenCredential credential, object clientOptions)
        {
            return _inner.CreateClient(clientType, configuration, credential, clientOptions);
        }

        private BrokerTokenCredential GetOrCreateBrokerCredential(string brokerEndpoint, string brokerApiKey)
        {
            var existing = _cachedCredential;
            if (existing != null)
            {
                return existing;
            }

            lock (_credentialLock)
            {
                if (_cachedCredential != null)
                {
                    return _cachedCredential;
                }

                _cachedCredential = new BrokerTokenCredential(brokerEndpoint, brokerApiKey);
                return _cachedCredential;
            }
        }
    }
}
