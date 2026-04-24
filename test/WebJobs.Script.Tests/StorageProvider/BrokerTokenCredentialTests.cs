// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using FluentAssertions;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.StorageProvider
{
    public class BrokerTokenCredentialTests
    {
        private const string TestEndpoint = "http://broker.internal.test";
        private const string TestApiKey = "test-api-key-12345";

        [Fact]
        public void Constructor_NullEndpoint_ThrowsArgumentException()
        {
            Action act = () => new BrokerTokenCredential(null, TestApiKey);
            act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("brokerEndpoint");
        }

        [Fact]
        public void Constructor_EmptyEndpoint_ThrowsArgumentException()
        {
            Action act = () => new BrokerTokenCredential("", TestApiKey);
            act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("brokerEndpoint");
        }

        [Fact]
        public void Constructor_NullApiKey_ThrowsArgumentException()
        {
            Action act = () => new BrokerTokenCredential(TestEndpoint, null);
            act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("apiKey");
        }

        [Fact]
        public void Constructor_EmptyApiKey_ThrowsArgumentException()
        {
            Action act = () => new BrokerTokenCredential(TestEndpoint, "");
            act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("apiKey");
        }

        [Fact]
        public void Constructor_ValidParameters_DoesNotThrow()
        {
            using var credential = new BrokerTokenCredential(TestEndpoint, TestApiKey);
            credential.Should().NotBeNull();
        }

        [Fact]
        public async Task GetTokenAsync_ValidBrokerResponse_ReturnsAccessToken()
        {
            var expiresOn = DateTimeOffset.UtcNow.AddHours(1);
            var handler = new MockHandler(new BrokerResponse
            {
                Token = "mock-access-token",
                ExpiresOn = expiresOn,
                StorageAccountName = "sttest",
            });

            using var httpClient = new HttpClient(handler);
            using var credential = new BrokerTokenCredential(TestEndpoint, TestApiKey, httpClient);

            var context = new TokenRequestContext(new[] { "https://storage.azure.com/.default" });
            var token = await credential.GetTokenAsync(context, CancellationToken.None);

            token.Token.Should().Be("mock-access-token");
            token.ExpiresOn.Should().Be(expiresOn);
        }

        [Fact]
        public async Task GetTokenAsync_CachesToken_DoesNotCallBrokerTwice()
        {
            var expiresOn = DateTimeOffset.UtcNow.AddHours(1);
            var handler = new CountingHandler(new BrokerResponse
            {
                Token = "cached-token",
                ExpiresOn = expiresOn,
            });

            using var httpClient = new HttpClient(handler);
            using var credential = new BrokerTokenCredential(TestEndpoint, TestApiKey, httpClient);

            var context = new TokenRequestContext(new[] { "https://storage.azure.com/.default" });
            await credential.GetTokenAsync(context, CancellationToken.None);
            await credential.GetTokenAsync(context, CancellationToken.None);

            handler.CallCount.Should().Be(1);
        }

        [Fact]
        public async Task GetTokenAsync_BrokerReturns401_ThrowsInvalidOperationException()
        {
            var handler = new FixedResponseHandler(HttpStatusCode.Unauthorized, "Unauthorized");

            using var httpClient = new HttpClient(handler);
            using var credential = new BrokerTokenCredential(TestEndpoint, TestApiKey, httpClient);

            var context = new TokenRequestContext(new[] { "https://storage.azure.com/.default" });
            Func<Task> act = () => credential.GetTokenAsync(context, CancellationToken.None).AsTask();

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*401*");
        }

        [Fact]
        public async Task GetTokenAsync_BrokerReturnsEmptyToken_ThrowsInvalidOperationException()
        {
            var handler = new MockHandler(new BrokerResponse
            {
                Token = "",
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(1),
            });

            using var httpClient = new HttpClient(handler);
            using var credential = new BrokerTokenCredential(TestEndpoint, TestApiKey, httpClient);

            var context = new TokenRequestContext(new[] { "https://storage.azure.com/.default" });
            Func<Task> act = () => credential.GetTokenAsync(context, CancellationToken.None).AsTask();

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*empty or null*");
        }

        [Fact]
        public async Task GetTokenAsync_SendsApiKeyHeader()
        {
            var handler = new HeaderCapturingHandler(new BrokerResponse
            {
                Token = "test-token",
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(1),
            });

            using var httpClient = new HttpClient(handler);
            using var credential = new BrokerTokenCredential(TestEndpoint, TestApiKey, httpClient);

            var context = new TokenRequestContext(new[] { "https://storage.azure.com/.default" });
            await credential.GetTokenAsync(context, CancellationToken.None);

            handler.CapturedApiKey.Should().Be(TestApiKey);
        }

        [Fact]
        public async Task GetTokenAsync_CallsCorrectEndpoint()
        {
            var handler = new UriCapturingHandler(new BrokerResponse
            {
                Token = "test-token",
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(1),
            });

            using var httpClient = new HttpClient(handler);
            using var credential = new BrokerTokenCredential(TestEndpoint, TestApiKey, httpClient);

            var context = new TokenRequestContext(new[] { "https://storage.azure.com/.default" });
            await credential.GetTokenAsync(context, CancellationToken.None);

            handler.CapturedUri.Should().Contain("/token/storage");
            handler.CapturedUri.Should().Contain("resource=");
        }

        [Fact]
        public async Task GetTokenAsync_V1Resource_AcceptsAndSucceeds()
        {
            var handler = new MockHandler(new BrokerResponse
            {
                Token = "v1-token",
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(1),
            });

            using var httpClient = new HttpClient(handler);
            using var credential = new BrokerTokenCredential(TestEndpoint, TestApiKey, httpClient);

            // V1 resource format (without /.default)
            var context = new TokenRequestContext(new[] { "https://storage.azure.com/" });
            var token = await credential.GetTokenAsync(context, CancellationToken.None);

            token.Token.Should().Be("v1-token");
        }

        [Fact]
        public async Task GetTokenAsync_NoScopes_ThrowsArgumentException()
        {
            using var credential = new BrokerTokenCredential(TestEndpoint, TestApiKey);

            var context = new TokenRequestContext(Array.Empty<string>());
            Func<Task> act = () => credential.GetTokenAsync(context, CancellationToken.None).AsTask();

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("*No scopes*");
        }

        [Fact]
        public async Task GetTokenAsync_UnsupportedScope_ThrowsArgumentException()
        {
            using var credential = new BrokerTokenCredential(TestEndpoint, TestApiKey);

            var context = new TokenRequestContext(new[] { "https://vault.azure.net/.default" });
            Func<Task> act = () => credential.GetTokenAsync(context, CancellationToken.None).AsTask();

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("*Unsupported scope*");
        }

        [Fact]
        public async Task GetTokenAsync_MultipleScopes_ThrowsArgumentException()
        {
            using var credential = new BrokerTokenCredential(TestEndpoint, TestApiKey);

            var context = new TokenRequestContext(new[] { "https://storage.azure.com/.default", "https://vault.azure.net/.default" });
            Func<Task> act = () => credential.GetTokenAsync(context, CancellationToken.None).AsTask();

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("*Multiple scopes*");
        }

        [Fact]
        public async Task GetTokenAsync_LongErrorBody_TruncatesTo200Chars()
        {
            var longError = new string('x', 500);
            var handler = new FixedResponseHandler(HttpStatusCode.InternalServerError, longError);

            using var httpClient = new HttpClient(handler);
            using var credential = new BrokerTokenCredential(TestEndpoint, TestApiKey, httpClient);

            var context = new TokenRequestContext(new[] { "https://storage.azure.com/.default" });
            Func<Task> act = () => credential.GetTokenAsync(context, CancellationToken.None).AsTask();

            var exception = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;

            // The original 500-char body should be truncated
            exception.Message.Should().Contain("...");
            exception.Message.Should().NotContain(longError);
        }

        #region Test Helpers

        private class BrokerResponse
        {
            public string Token { get; set; }
            public DateTimeOffset ExpiresOn { get; set; }
            public string StorageAccountName { get; set; }
        }

        private static HttpResponseMessage CreateBrokerResponse(BrokerResponse response)
        {
            var json = JsonSerializer.Serialize(new
            {
                token = response.Token,
                expiresOn = response.ExpiresOn,
                storageAccountName = response.StorageAccountName ?? "sttest",
                blobEndpoint = $"https://{response.StorageAccountName ?? "sttest"}.blob.core.windows.net",
                queueEndpoint = $"https://{response.StorageAccountName ?? "sttest"}.queue.core.windows.net",
                tableEndpoint = $"https://{response.StorageAccountName ?? "sttest"}.table.core.windows.net",
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        }

        private class MockHandler : HttpMessageHandler
        {
            private readonly BrokerResponse _response;

            public MockHandler(BrokerResponse response) => _response = response;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(CreateBrokerResponse(_response));
            }
        }

        private class CountingHandler : HttpMessageHandler
        {
            private readonly BrokerResponse _response;
            private int _callCount;

            public CountingHandler(BrokerResponse response) => _response = response;

            public int CallCount => _callCount;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _callCount);
                return Task.FromResult(CreateBrokerResponse(_response));
            }
        }

        private class HeaderCapturingHandler : HttpMessageHandler
        {
            private readonly BrokerResponse _response;

            public HeaderCapturingHandler(BrokerResponse response) => _response = response;

            public string CapturedApiKey { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.Headers.TryGetValues("X-Broker-Key", out var values))
                {
                    CapturedApiKey = string.Join(",", values);
                }

                return Task.FromResult(CreateBrokerResponse(_response));
            }
        }

        private class UriCapturingHandler : HttpMessageHandler
        {
            private readonly BrokerResponse _response;

            public UriCapturingHandler(BrokerResponse response) => _response = response;

            public string CapturedUri { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                CapturedUri = request.RequestUri.ToString();
                return Task.FromResult(CreateBrokerResponse(_response));
            }
        }

        private class FixedResponseHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _statusCode;
            private readonly string _body;

            public FixedResponseHandler(HttpStatusCode statusCode, string body)
            {
                _statusCode = statusCode;
                _body = body;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(_statusCode)
                {
                    Content = new StringContent(_body)
                });
            }
        }

        #endregion
    }
}
