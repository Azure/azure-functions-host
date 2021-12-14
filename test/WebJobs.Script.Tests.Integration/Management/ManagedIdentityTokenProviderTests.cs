// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Management.LinuxSpecialization;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Moq.Protected;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Management
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.ContainerInstanceTests)]
    public class ManagedIdentityTokenProviderTests
    {
        private const string UriWithNoSasToken = "https://storageaccount.blob.core.windows.net/funcs/hello.zip";
        private const string UriWithNoSasTokenHost = "https://storageaccount.blob.core.windows.net";
        private const string MsiEndpoint = "http://localhost:8081/msi/token";
        private const string UserAssignedIdentity =
            "/subscriptions/aaaabbbb-cccc-dddd-eeee-fffgggghhh/resourcegroups/rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id1";
        private const string MSISecret = "msi-secret";
        private const string AccessToken = "access-token";

        private readonly TestEnvironment _environment;
        private IHttpClientFactory _httpClientFactory;

        public ManagedIdentityTokenProviderTests()
        {
            _environment = new TestEnvironment();
            _httpClientFactory = TestHelpers.CreateHttpClientFactory();
        }

        private static TokenServiceMsiResponse GetTokenServiceMsiResponse()
        {
            return new TokenServiceMsiResponse()
            {
                AccessToken = AccessToken
            };
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData(EnvironmentSettingNames.SystemAssignedManagedIdentity)]
        public async Task UsesSystemAssignedIdentityToFetchToken(string resourceId)
        {
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.MsiEndpoint, MsiEndpoint);
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.MsiSecret, MSISecret);
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.RunFromPackageManagedResourceId, resourceId);

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(s => MatchesVerb(s, HttpMethod.Get) && UrlMatchesSystemAssignedIdentity(s, UriWithNoSasTokenHost)),
                ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonConvert.SerializeObject(GetTokenServiceMsiResponse()))
            });

            _httpClientFactory = TestHelpers.CreateHttpClientFactory(handlerMock.Object);

            var tokenProvider = new ManagedIdentityTokenProvider(_environment, _httpClientFactory, new TestMetricsLogger(), NullLogger<ManagedIdentityTokenProvider>.Instance);
            var token = await tokenProvider.GetManagedIdentityToken(UriWithNoSasToken);
            Assert.Equal(AccessToken, token);
        }

        [Fact]
        public async Task UsesUserAssignedIdentityToFetchToken()
        {
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.MsiEndpoint, MsiEndpoint);
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.MsiSecret, MSISecret);
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.RunFromPackageManagedResourceId, UserAssignedIdentity);

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(s => MatchesVerb(s, HttpMethod.Get) && UrlMatchesUserAssignedIdentity(s, UriWithNoSasTokenHost, UserAssignedIdentity)),
                ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonConvert.SerializeObject(GetTokenServiceMsiResponse()))
            });

            _httpClientFactory = TestHelpers.CreateHttpClientFactory(handlerMock.Object);

            var tokenProvider = new ManagedIdentityTokenProvider(_environment, _httpClientFactory, new TestMetricsLogger(), NullLogger<ManagedIdentityTokenProvider>.Instance);
            var token = await tokenProvider.GetManagedIdentityToken(UriWithNoSasToken);
            Assert.Equal(AccessToken, token);
        }

        [Fact]
        public async Task ThrowsIfMSINotEnabled()
        {
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.MsiEndpoint, string.Empty);
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.MsiSecret, string.Empty);

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(s => MatchesVerb(s, HttpMethod.Get) && UrlMatchesSystemAssignedIdentity(s, UriWithNoSasTokenHost)),
                ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonConvert.SerializeObject(GetTokenServiceMsiResponse()))
            });

            _httpClientFactory = TestHelpers.CreateHttpClientFactory(handlerMock.Object);

            var tokenProvider = new ManagedIdentityTokenProvider(_environment, _httpClientFactory, new TestMetricsLogger(),
                NullLogger<ManagedIdentityTokenProvider>.Instance);

            await Assert.ThrowsAsync<InvalidOperationException>(async () => await tokenProvider.GetManagedIdentityToken(UriWithNoSasToken));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("storageaccount")]
        public async Task ThrowsOnInvalidUrls(string url)
        {
            var tokenProvider = new ManagedIdentityTokenProvider(_environment, _httpClientFactory, new TestMetricsLogger(), NullLogger<ManagedIdentityTokenProvider>.Instance);
            await Assert.ThrowsAsync<ArgumentException>(async () => await tokenProvider.GetManagedIdentityToken(url));
        }

        private static bool MatchesVerb(HttpRequestMessage s, HttpMethod httpMethod)
        {
            return s.Method == httpMethod;
        }

        private static bool UrlMatchesSystemAssignedIdentity(HttpRequestMessage r, string resourceHost)
        {
            return r.RequestUri.AbsoluteUri.Contains($"&resource={resourceHost}") && !r.RequestUri.AbsoluteUri.Contains("&mi_res_id=");
        }

        private static bool UrlMatchesUserAssignedIdentity(HttpRequestMessage r, string resourceHost, string resourceId)
        {
            return r.RequestUri.AbsoluteUri.Contains($"&resource={resourceHost}") && r.RequestUri.AbsoluteUri.Contains($"&mi_res_id={resourceId}");
        }
    }
}
