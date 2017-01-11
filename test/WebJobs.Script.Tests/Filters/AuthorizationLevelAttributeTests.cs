// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Dependencies;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class AuthorizationLevelAttributeTests
    {
        private const string TestHostFunctionKeyValue1 = "jkl012";
        private const string TestHostFunctionKeyValue2 = "mno345";
        private const string TestFunctionKeyValue1 = "def456";
        private const string TestFunctionKeyValue2 = "ghi789";

        private readonly string testMasterKeyValue = "abc123";

        private HttpActionContext _actionContext;
        private HostSecretsInfo _hostSecrets;
        private Dictionary<string, string> _functionSecrets;
        private Mock<ISecretManager> _mockSecretManager;

        public AuthorizationLevelAttributeTests()
        {
            _actionContext = new HttpActionContext();
            HttpControllerContext controllerContext = new HttpControllerContext();
            _actionContext.ControllerContext = controllerContext;
            HttpConfiguration httpConfig = new HttpConfiguration();
            controllerContext.Configuration = httpConfig;
            Mock<IDependencyResolver> mockDependencyResolver = new Mock<IDependencyResolver>(MockBehavior.Strict);
            httpConfig.DependencyResolver = mockDependencyResolver.Object;
            _mockSecretManager = new Mock<ISecretManager>(MockBehavior.Strict);
            _hostSecrets = new HostSecretsInfo
            {
                MasterKey = testMasterKeyValue,
                FunctionKeys = new Dictionary<string, string>
                {
                    { "1", TestHostFunctionKeyValue1 },
                    { "2", TestHostFunctionKeyValue2 }
                }
            };
            _mockSecretManager.Setup(p => p.GetHostSecretsAsync()).ReturnsAsync(_hostSecrets);
            _functionSecrets = new Dictionary<string, string>
            {
                { "1",  TestFunctionKeyValue1 },
                { "2",  TestFunctionKeyValue2 }
            };
            _mockSecretManager.Setup(p => p.GetFunctionSecretsAsync(It.IsAny<string>(), false)).ReturnsAsync(_functionSecrets);
            mockDependencyResolver.Setup(p => p.GetService(typeof(ISecretManager))).Returns(_mockSecretManager.Object);
        }

        [Fact]
        public async Task OnAuthorization_AdminLevel_ValidHeader_Succeeds()
        {
            AuthorizationLevelAttribute attribute = new AuthorizationLevelAttribute(AuthorizationLevel.Admin);

            HttpRequestMessage request = new HttpRequestMessage();
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, "abc123");
            _actionContext.ControllerContext.Request = request;

            await attribute.OnAuthorizationAsync(_actionContext, CancellationToken.None);

            Assert.Null(_actionContext.Response);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("invalid")]
        public async Task OnAuthorization_AdminLevel_InvalidHeader_ReturnsUnauthorized(string headerValue)
        {
            AuthorizationLevelAttribute attribute = new AuthorizationLevelAttribute(AuthorizationLevel.Admin);

            HttpRequestMessage request = new HttpRequestMessage();
            if (headerValue != null)
            {
                request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, headerValue);
            }
            _actionContext.ControllerContext.Request = request;

            await attribute.OnAuthorizationAsync(_actionContext, CancellationToken.None);

            HttpResponseMessage response = _actionContext.Response;
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task OnAuthorization_AdminLevel_NoMasterKeySet_ReturnsUnauthorized()
        {
            AuthorizationLevelAttribute attribute = new AuthorizationLevelAttribute(AuthorizationLevel.Admin);
            _hostSecrets.MasterKey = null;

            HttpRequestMessage request = new HttpRequestMessage();
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, testMasterKeyValue);
            _actionContext.ControllerContext.Request = request;

            await attribute.OnAuthorizationAsync(_actionContext, CancellationToken.None);

            HttpResponseMessage response = _actionContext.Response;
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task OnAuthorization_AnonymousLevel_Succeeds()
        {
            AuthorizationLevelAttribute attribute = new AuthorizationLevelAttribute(AuthorizationLevel.Anonymous);

            _actionContext.ControllerContext.Request = new HttpRequestMessage();

            await attribute.OnAuthorizationAsync(_actionContext, CancellationToken.None);

            Assert.Null(_actionContext.Response);
        }

        [Fact]
        public async Task GetAuthorizationLevel_ValidKeyHeader_MasterKey_ReturnsAdmin()
        {
            HttpRequestMessage request = new HttpRequestMessage();
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, testMasterKeyValue);

            AuthorizationLevel level = await AuthorizationLevelAttribute.GetAuthorizationLevelAsync(request, _mockSecretManager.Object);

            Assert.Equal(AuthorizationLevel.Admin, level);
        }

        [Theory]
        [InlineData(TestHostFunctionKeyValue1, TestFunctionKeyValue1)]
        [InlineData(TestHostFunctionKeyValue2, TestFunctionKeyValue2)]
        public async Task GetAuthorizationLevel_ValidKeyHeader_FunctionKey_ReturnsFunction(string hostFunctionKeyValue, string functionKeyValue)
        {
            // first verify the host level function key works
            HttpRequestMessage request = new HttpRequestMessage();
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, hostFunctionKeyValue);
            AuthorizationLevel level = await AuthorizationLevelAttribute.GetAuthorizationLevelAsync(request, _mockSecretManager.Object);
            Assert.Equal(AuthorizationLevel.Function, level);

            // test function specific key
            request = new HttpRequestMessage();
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, functionKeyValue);
            level = await AuthorizationLevelAttribute.GetAuthorizationLevelAsync(request, _mockSecretManager.Object, functionName: "TestFunction");
            Assert.Equal(AuthorizationLevel.Function, level);
        }

        [Fact]
        public async Task GetAuthorizationLevel_InvalidKeyHeader_ReturnsAnonymous()
        {
            HttpRequestMessage request = new HttpRequestMessage();
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, "invalid");

            AuthorizationLevel level = await AuthorizationLevelAttribute.GetAuthorizationLevelAsync(request, _mockSecretManager.Object);

            Assert.Equal(AuthorizationLevel.Anonymous, level);
        }

        [Fact]
        public async Task GetAuthorizationLevel_ValidCodeQueryParam_MasterKey_ReturnsAdmin()
        {
            Uri uri = new Uri(string.Format("http://functions/api/foo?code={0}", testMasterKeyValue));
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

            AuthorizationLevel level = await AuthorizationLevelAttribute.GetAuthorizationLevelAsync(request, _mockSecretManager.Object);

            Assert.Equal(AuthorizationLevel.Admin, level);
        }

        [Theory]
        [InlineData(TestHostFunctionKeyValue1, TestFunctionKeyValue1)]
        [InlineData(TestHostFunctionKeyValue2, TestFunctionKeyValue2)]
        public async Task GetAuthorizationLevel_ValidCodeQueryParam_FunctionKey_ReturnsFunction(string hostFunctionKeyValue, string functionKeyValue)
        {
            // first try host level function key
            Uri uri = new Uri(string.Format("http://functions/api/foo?code={0}", hostFunctionKeyValue));
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            AuthorizationLevel level = await AuthorizationLevelAttribute.GetAuthorizationLevelAsync(request, _mockSecretManager.Object, functionName: "TestFunction");
            Assert.Equal(AuthorizationLevel.Function, level);

            uri = new Uri(string.Format("http://functions/api/foo?code={0}", functionKeyValue));
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            level = await AuthorizationLevelAttribute.GetAuthorizationLevelAsync(request, _mockSecretManager.Object, functionName: "TestFunction");
            Assert.Equal(AuthorizationLevel.Function, level);
        }

        [Fact]
        public async Task GetAuthorizationLevel_InvalidCodeQueryParam_ReturnsAnonymous()
        {
            Uri uri = new Uri(string.Format("http://functions/api/foo?code={0}", "invalid"));
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

            AuthorizationLevel level = await AuthorizationLevelAttribute.GetAuthorizationLevelAsync(request, _mockSecretManager.Object);

            Assert.Equal(AuthorizationLevel.Anonymous, level);
        }
    }
}
