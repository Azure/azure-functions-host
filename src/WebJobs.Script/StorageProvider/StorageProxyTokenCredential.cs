// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// A <see cref="TokenCredential"/> for the storage egress proxy model. Unlike <see cref="BrokerTokenCredential"/>,
    /// it does NOT call the broker to obtain a storage token. Instead it supplies the broker API key as the token
    /// value: the Azure Storage SDK stamps it on the <c>Authorization</c> header, the storage gateway authenticates
    /// on it, and the gateway replaces it with a real storage token server-side. The host therefore never holds a
    /// real storage token. Storage traffic is pointed at the gateway via the connection section's service URIs.
    /// </summary>
    internal sealed class StorageProxyTokenCredential : TokenCredential
    {
        // The broker API key does not expire like a JWT; a long, finite lifetime avoids DateTimeOffset.MaxValue
        // edge cases in the SDK's refresh math while never expiring in practice within a process lifetime.
        private static readonly TimeSpan SyntheticTokenLifetime = TimeSpan.FromDays(365);

        private readonly string _apiKey;

        /// <summary>
        /// Initializes a new instance of the <see cref="StorageProxyTokenCredential"/> class.
        /// </summary>
        /// <param name="apiKey">The per-app broker API key presented to the storage gateway as the bearer carrier.</param>
        public StorageProxyTokenCredential(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("Parameter 'apiKey' cannot be null, empty, or whitespace.", nameof(apiKey));
            }

            _apiKey = apiKey;
        }

        /// <inheritdoc/>
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            return new AccessToken(_apiKey, DateTimeOffset.UtcNow.Add(SyntheticTokenLifetime));
        }

        /// <inheritdoc/>
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            return new ValueTask<AccessToken>(GetToken(requestContext, cancellationToken));
        }
    }
}
