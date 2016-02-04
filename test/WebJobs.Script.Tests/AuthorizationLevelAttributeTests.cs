using System;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Dependencies;
using Microsoft.Azure.WebJobs.Script;
using Moq;
using WebJobs.Script.WebHost;
using WebJobs.Script.WebHost.Filters;
using Xunit;

namespace WebJobs.Script.Tests
{
    public class AuthorizationLevelAttributeTests
    {
        private readonly string TestMasterKeyValue = "abc123";
        private readonly string TestFunctionKeyValue = "def456";
        private readonly string TestHostFunctionKeyValue = "xyz789";
        private HttpActionContext _actionContext;
        private HostSecrets _hostSecrets;
        private FunctionSecrets _functionSecrets;
        private Mock<SecretManager> _mockSecretManager;

        public AuthorizationLevelAttributeTests()
        {
            _actionContext = new HttpActionContext();
            HttpControllerContext controllerContext = new HttpControllerContext();
            _actionContext.ControllerContext = controllerContext;
            HttpConfiguration httpConfig = new HttpConfiguration();
            controllerContext.Configuration = httpConfig;
            Mock<IDependencyResolver> mockDependencyResolver = new Mock<IDependencyResolver>(MockBehavior.Strict);
            httpConfig.DependencyResolver = mockDependencyResolver.Object;
            _mockSecretManager = new Mock<SecretManager>(MockBehavior.Strict);
            _hostSecrets = new HostSecrets
            {
                MasterKey = TestMasterKeyValue,
                FunctionKey = TestHostFunctionKeyValue
            };
            _mockSecretManager.Setup(p => p.GetHostSecrets()).Returns(_hostSecrets);
            _functionSecrets = new FunctionSecrets
            {
                Key = TestFunctionKeyValue
            };
            _mockSecretManager.Setup(p => p.GetFunctionSecrets(It.IsAny<string>())).Returns(_functionSecrets);
            mockDependencyResolver.Setup(p => p.GetService(typeof(SecretManager))).Returns(_mockSecretManager.Object);
        }

        [Fact]
        public void OnAuthorization_AdminLevel_ValidHeader_Succeeds()
        {
            AuthorizationLevelAttribute attribute = new AuthorizationLevelAttribute(AuthorizationLevel.Admin);

            HttpRequestMessage request = new HttpRequestMessage();
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, "abc123");
            _actionContext.ControllerContext.Request = request;

            attribute.OnAuthorization(_actionContext);

            Assert.Null(_actionContext.Response);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("invalid")]
        public void OnAuthorization_AdminLevel_InvalidHeader_ReturnsUnauthorized(string headerValue)
        {
            AuthorizationLevelAttribute attribute = new AuthorizationLevelAttribute(AuthorizationLevel.Admin);

            HttpRequestMessage request = new HttpRequestMessage();
            if (headerValue != null)
            {
                request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, headerValue);
            }
            _actionContext.ControllerContext.Request = request;

            attribute.OnAuthorization(_actionContext);

            HttpResponseMessage response = _actionContext.Response;
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public void OnAuthorization_AdminLevel_NoMasterKeySet_ReturnsUnauthorized()
        {
            AuthorizationLevelAttribute attribute = new AuthorizationLevelAttribute(AuthorizationLevel.Admin);
            _hostSecrets.MasterKey = null;

            HttpRequestMessage request = new HttpRequestMessage();
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, TestMasterKeyValue);
            _actionContext.ControllerContext.Request = request;

            attribute.OnAuthorization(_actionContext);

            HttpResponseMessage response = _actionContext.Response;
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public void OnAuthorization_AnonymousLevel_Succeeds()
        {
            AuthorizationLevelAttribute attribute = new AuthorizationLevelAttribute(AuthorizationLevel.Anonymous);

            _actionContext.ControllerContext.Request = new HttpRequestMessage();

            attribute.OnAuthorization(_actionContext);

            Assert.Null(_actionContext.Response);
        }

        [Fact]
        public void GetAuthorizationLevel_ValidKeyHeader_MasterKey_ReturnsAdmin()
        {
            HttpRequestMessage request = new HttpRequestMessage();
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, TestMasterKeyValue);

            AuthorizationLevel level = AuthorizationLevelAttribute.GetAuthorizationLevel(request, _mockSecretManager.Object);

            Assert.Equal(AuthorizationLevel.Admin, level);
        }

        [Fact]
        public void GetAuthorizationLevel_ValidKeyHeader_FunctionKey_ReturnsFunction()
        {
            // first verify the host level function key works
            HttpRequestMessage request = new HttpRequestMessage();
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, TestHostFunctionKeyValue);
            AuthorizationLevel level = AuthorizationLevelAttribute.GetAuthorizationLevel(request, _mockSecretManager.Object);
            Assert.Equal(AuthorizationLevel.Function, level);

            // test function specific key
            request = new HttpRequestMessage();
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, TestFunctionKeyValue);
            level = AuthorizationLevelAttribute.GetAuthorizationLevel(request, _mockSecretManager.Object, functionName: "TestFunction");
            Assert.Equal(AuthorizationLevel.Function, level);
        }

        [Fact]
        public void GetAuthorizationLevel_InvalidKeyHeader_ReturnsAnonymous()
        {
            HttpRequestMessage request = new HttpRequestMessage();
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, "invalid");

            AuthorizationLevel level = AuthorizationLevelAttribute.GetAuthorizationLevel(request, _mockSecretManager.Object);

            Assert.Equal(AuthorizationLevel.Anonymous, level);
        }

        [Fact]
        public void GetAuthorizationLevel_ValidKeyQueryParam_MasterKey_ReturnsAdmin()
        {
            Uri uri = new Uri(string.Format("http://functions/api/foo?key={0}", TestMasterKeyValue));
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

            AuthorizationLevel level = AuthorizationLevelAttribute.GetAuthorizationLevel(request, _mockSecretManager.Object);

            Assert.Equal(AuthorizationLevel.Admin, level);
        }

        [Fact]
        public void GetAuthorizationLevel_ValidKeyQueryParam_FunctionKey_ReturnsFunction()
        {
            // first try host level function key
            Uri uri = new Uri(string.Format("http://functions/api/foo?key={0}", TestHostFunctionKeyValue));
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            AuthorizationLevel level = AuthorizationLevelAttribute.GetAuthorizationLevel(request, _mockSecretManager.Object, functionName: "TestFunction");
            Assert.Equal(AuthorizationLevel.Function, level);

            uri = new Uri(string.Format("http://functions/api/foo?key={0}", TestFunctionKeyValue));
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            level = AuthorizationLevelAttribute.GetAuthorizationLevel(request, _mockSecretManager.Object, functionName: "TestFunction");
            Assert.Equal(AuthorizationLevel.Function, level);
        }

        [Fact]
        public void GetAuthorizationLevel_InvalidKeyQueryParam_ReturnsAnonymous()
        {
            Uri uri = new Uri(string.Format("http://functions/api/foo?key={0}", "invalid"));
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

            AuthorizationLevel level = AuthorizationLevelAttribute.GetAuthorizationLevel(request, _mockSecretManager.Object);

            Assert.Equal(AuthorizationLevel.Anonymous, level);
        }
    }
}
