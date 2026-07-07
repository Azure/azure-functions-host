// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using FluentAssertions;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.StorageProvider
{
    public class StorageProxyTokenCredentialTests
    {
        private const string TestApiKey = "test-broker-key";

        private static readonly TokenRequestContext StorageContext =
            new TokenRequestContext(new[] { "https://storage.azure.com/.default" });

        [Fact]
        public void Constructor_NullApiKey_ThrowsArgumentException()
        {
            Action act = () => new StorageProxyTokenCredential(null);
            act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("apiKey");
        }

        [Fact]
        public void Constructor_EmptyApiKey_ThrowsArgumentException()
        {
            Action act = () => new StorageProxyTokenCredential(string.Empty);
            act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("apiKey");
        }

        [Fact]
        public void GetToken_ReturnsApiKeyAsBearerCarrier()
        {
            var credential = new StorageProxyTokenCredential(TestApiKey);

            var token = credential.GetToken(StorageContext, CancellationToken.None);

            token.Token.Should().Be(TestApiKey);
            token.ExpiresOn.Should().BeAfter(DateTimeOffset.UtcNow);
        }

        [Fact]
        public async Task GetTokenAsync_ReturnsApiKeyAsBearerCarrier()
        {
            var credential = new StorageProxyTokenCredential(TestApiKey);

            var token = await credential.GetTokenAsync(StorageContext, CancellationToken.None);

            token.Token.Should().Be(TestApiKey);
            token.ExpiresOn.Should().BeAfter(DateTimeOffset.UtcNow);
        }

        [Fact]
        public void GetToken_IgnoresRequestedScope()
        {
            var credential = new StorageProxyTokenCredential(TestApiKey);

            // The gateway mints the real token, so any requested scope resolves to the same broker-key carrier.
            var token = credential.GetToken(new TokenRequestContext(new[] { "https://example.invalid/.default" }), CancellationToken.None);

            token.Token.Should().Be(TestApiKey);
        }
    }
}
