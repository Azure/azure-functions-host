// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// A <see cref="TokenCredential"/> that acquires storage tokens from the Token Broker sidecar.
    /// Used in the HOBO (Hosted On Behalf Of) model where each app gets isolated storage via a
    /// token broker that holds per-app managed identities.
    /// Only supports the <c>https://storage.azure.com/.default</c> scope.
    /// </summary>
    internal sealed class BrokerTokenCredential : TokenCredential, IDisposable
    {
        private const string ApiKeyHeaderName = "X-Broker-Key";
        private const string StorageScope = "https://storage.azure.com/.default";
        private const string StorageResource = "https://storage.azure.com/";

        private static readonly TimeSpan RefreshBuffer = TimeSpan.FromMinutes(5);

        private readonly HttpClient _httpClient;
        private readonly Uri _brokerEndpoint;
        private readonly string _apiKey;
        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(initialCount: 1, maxCount: 1);
        private readonly bool _ownsHttpClient;

        private CachedAccessToken _cachedToken;

        /// <summary>
        /// Initializes a new instance of the <see cref="BrokerTokenCredential"/> class.
        /// </summary>
        /// <param name="brokerEndpoint">The broker base URL.</param>
        /// <param name="apiKey">The per-app API key for broker authentication.</param>
        /// <param name="httpClient">Optional HTTP client (for testing). If null, a new one is created and owned.</param>
        public BrokerTokenCredential(string brokerEndpoint, string apiKey, HttpClient httpClient = null)
        {
            if (string.IsNullOrWhiteSpace(brokerEndpoint))
            {
                throw new ArgumentException("Parameter 'brokerEndpoint' cannot be null, empty, or whitespace.", nameof(brokerEndpoint));
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("Parameter 'apiKey' cannot be null, empty, or whitespace.", nameof(apiKey));
            }

            _brokerEndpoint = new Uri(brokerEndpoint.TrimEnd('/'));
            _apiKey = apiKey;
            _ownsHttpClient = httpClient == null;
            _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        /// <inheritdoc/>
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            return GetTokenAsync(requestContext, cancellationToken)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        /// <inheritdoc/>
        public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            ValidateScope(requestContext);

            var cached = Volatile.Read(ref _cachedToken);
            if (cached != null && cached.ExpiresOn > DateTimeOffset.UtcNow.Add(RefreshBuffer))
            {
                return cached.ToAccessToken();
            }

            await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                // Double-check after acquiring the lock to avoid redundant broker calls.
                cached = Volatile.Read(ref _cachedToken);
                if (cached != null && cached.ExpiresOn > DateTimeOffset.UtcNow.Add(RefreshBuffer))
                {
                    return cached.ToAccessToken();
                }

                var tokenResponse = await CallBrokerAsync(cancellationToken).ConfigureAwait(false);
                var newCached = new CachedAccessToken(tokenResponse.Token, tokenResponse.ExpiresOn);
                Volatile.Write(ref _cachedToken, newCached);

                return newCached.ToAccessToken();
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_ownsHttpClient)
            {
                _httpClient.Dispose();
            }

            _refreshLock.Dispose();
        }

        private static void ValidateScope(TokenRequestContext requestContext)
        {
            if (requestContext.Scopes == null || requestContext.Scopes.Length == 0)
            {
                throw new ArgumentException(
                    "No scopes provided. Only 'https://storage.azure.com/.default' is supported.",
                    nameof(requestContext));
            }

            if (requestContext.Scopes.Length > 1)
            {
                throw new ArgumentException(
                    $"Multiple scopes not supported. Only 'https://storage.azure.com/.default' is supported. Requested: '{string.Join(", ", requestContext.Scopes)}'.",
                    nameof(requestContext));
            }

            var scope = requestContext.Scopes[0];
            bool isValidScope = string.Equals(scope, StorageScope, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(scope, StorageResource, StringComparison.OrdinalIgnoreCase);

            if (!isValidScope)
            {
                throw new ArgumentException(
                    $"Unsupported scope '{scope}'. Only 'https://storage.azure.com/.default' or 'https://storage.azure.com/' is supported.",
                    nameof(requestContext));
            }
        }

        private async Task<BrokerTokenResponse> CallBrokerAsync(CancellationToken cancellationToken)
        {
            var requestUri = new Uri(_brokerEndpoint, "/token/storage?resource=" + Uri.EscapeDataString(StorageScope));

            using (var request = new HttpRequestMessage(HttpMethod.Get, requestUri))
            {
                request.Headers.Add(ApiKeyHeaderName, _apiKey);

                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    // Truncate error body to avoid leaking sensitive data in logs/exceptions.
                    var truncatedBody = errorBody.Length > 200 ? errorBody.Substring(0, 200) + "..." : errorBody;

                    throw new InvalidOperationException(
                        $"Token broker returned HTTP '{(int)response.StatusCode}'. Response: '{truncatedBody}'.");
                }

                var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var tokenResponse = JsonSerializer.Deserialize<BrokerTokenResponse>(responseBody);

                if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.Token))
                {
                    throw new InvalidOperationException("Token broker returned an empty or null token.");
                }

                return tokenResponse;
            }
        }

        private sealed class BrokerTokenResponse
        {
            [JsonPropertyName("token")]
            public string Token { get; set; }

            [JsonPropertyName("expiresOn")]
            public DateTimeOffset ExpiresOn { get; set; }

            [JsonPropertyName("storageAccountName")]
            public string StorageAccountName { get; set; }

            [JsonPropertyName("blobEndpoint")]
            public string BlobEndpoint { get; set; }

            [JsonPropertyName("queueEndpoint")]
            public string QueueEndpoint { get; set; }

            [JsonPropertyName("tableEndpoint")]
            public string TableEndpoint { get; set; }
        }

        private sealed class CachedAccessToken
        {
            public CachedAccessToken(string token, DateTimeOffset expiresOn)
            {
                Token = token;
                ExpiresOn = expiresOn;
            }

            public string Token { get; }

            public DateTimeOffset ExpiresOn { get; }

            public AccessToken ToAccessToken() => new AccessToken(Token, ExpiresOn);
        }
    }
}
