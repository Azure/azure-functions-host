// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Azure.Core;
using Azure.Core.Pipeline;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// A decorator around <see cref="AzureComponentFactory"/> that adds support for the "Broker" credential type.
    /// When a configuration section has <c>credential=Broker</c>, this factory creates a <see cref="BrokerTokenCredential"/>
    /// that acquires storage tokens from a token broker sidecar. When a section has <c>credential=StorageProxy</c>
    /// (the storage egress proxy model), it creates a <see cref="StorageProxyTokenCredential"/> that supplies the broker
    /// key as the bearer carrier while storage traffic is pointed at the storage gateway, which injects the real token
    /// server-side. For all other credential types, it delegates to the inner factory.
    /// </summary>
    internal sealed class BrokerAwareComponentFactory : AzureComponentFactory
    {
        private const string CredentialKey = "credential";
        private const string BrokerCredentialValue = "Broker";
        private const string StorageProxyCredentialValue = "StorageProxy";
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

            if (string.Equals(credentialType, StorageProxyCredentialValue, StringComparison.OrdinalIgnoreCase))
            {
                var proxyApiKey = configuration[BrokerApiKeyKey];

                if (string.IsNullOrEmpty(proxyApiKey))
                {
                    throw new InvalidOperationException(
                        $"Storage credential type is '{StorageProxyCredentialValue}' but '{BrokerApiKeyKey}' is not configured.");
                }

                // Storage egress proxy: supply the broker key as the bearer carrier and let the storage gateway
                // inject the real token server-side. The host never holds a real storage token in this model.
                return new StorageProxyTokenCredential(proxyApiKey);
            }

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
            var options = _inner.CreateClientOptions(optionsType, serviceVersion, configuration);

            // Storage egress proxy: the service URI stays the REAL account URL so the Azure Storage SDK composes
            // correct /{container}/{blob} paths (it does not support a path prefix on the service URI). This policy
            // redirects the transport to the gateway, preserving the full path, so the gateway injects the real
            // storage token server-side.
            if (configuration != null &&
                string.Equals(configuration[CredentialKey], StorageProxyCredentialValue, StringComparison.OrdinalIgnoreCase) &&
                options is ClientOptions clientOptions)
            {
                var gatewayEndpoint = configuration[BrokerEndpointKey];

                if (string.IsNullOrWhiteSpace(gatewayEndpoint))
                {
                    throw new InvalidOperationException(
                        $"Storage credential type is '{StorageProxyCredentialValue}' but '{BrokerEndpointKey}' is not configured.");
                }

                clientOptions.AddPolicy(
                    new StorageProxyUriRewritePolicy(new Uri(gatewayEndpoint.TrimEnd('/'))),
                    HttpPipelinePosition.PerCall);
            }

            return options;
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

        /// <summary>
        /// A per-call pipeline policy that redirects an outgoing Azure Storage request to the storage gateway in the
        /// <c>StorageProxy</c> model. The SDK composes the request against the real service URI
        /// (<c>https://{account}.{service}.{suffix}/{container}/{blob}</c>); this policy rewrites the transport to
        /// <c>{gateway}/{service}/{container}/{blob}</c>, taking the service from the original host's second label
        /// and preserving the path and query. The gateway authenticates the broker key and injects a real token.
        /// </summary>
        private sealed class StorageProxyUriRewritePolicy : HttpPipelineSynchronousPolicy
        {
            private static readonly HashSet<string> KnownStorageServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "blob", "queue", "table", "file",
            };

            private readonly Uri _gatewayBaseUri;

            public StorageProxyUriRewritePolicy(Uri gatewayBaseUri)
            {
                _gatewayBaseUri = gatewayBaseUri;
            }

            public override void OnSendingRequest(HttpMessage message)
            {
                var requestUri = message.Request.Uri;
                var hostLabels = requestUri.Host.Split('.');

                // Only rewrite standard storage hosts ({account}.{service}.{suffix}) whose second label is a known
                // Azure Storage service; leave anything else untouched so non-storage clients or unusual hosts
                // (IP/localhost/custom domain) are never mis-routed to the gateway.
                if (hostLabels.Length < 3 || !KnownStorageServices.Contains(hostLabels[1]))
                {
                    return;
                }

                var service = hostLabels[1];
                var originalPath = requestUri.Path;
                var originalQuery = requestUri.Query;

                // Preserve any base path on the configured gateway endpoint (e.g. https://host/proxy).
                var gatewayBasePath = _gatewayBaseUri.AbsolutePath.TrimEnd('/');

                requestUri.Reset(_gatewayBaseUri);
                requestUri.Path = string.Concat(gatewayBasePath, "/", service, "/", originalPath.TrimStart('/'));
                requestUri.Query = originalQuery;
            }
        }
    }
}
