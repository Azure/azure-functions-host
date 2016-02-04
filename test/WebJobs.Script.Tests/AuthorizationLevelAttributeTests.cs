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
        private HttpActionContext _actionContext;
        private HostSecrets _hostSecrets;

        public AuthorizationLevelAttributeTests()
        {
            _actionContext = new HttpActionContext();
            HttpControllerContext controllerContext = new HttpControllerContext();
            _actionContext.ControllerContext = controllerContext;
            HttpConfiguration httpConfig = new HttpConfiguration();
            controllerContext.Configuration = httpConfig;
            Mock<IDependencyResolver> mockDependencyResolver = new Mock<IDependencyResolver>(MockBehavior.Strict);
            httpConfig.DependencyResolver = mockDependencyResolver.Object;
            Mock<SecretManager> mockSecretManager = new Mock<SecretManager>(MockBehavior.Strict);
            _hostSecrets = new HostSecrets
            {
                MasterKey = TestMasterKeyValue
            };
            mockSecretManager.Setup(p => p.GetHostSecrets()).Returns(_hostSecrets);
            mockDependencyResolver.Setup(p => p.GetService(typeof(SecretManager))).Returns(mockSecretManager.Object);
        }

        [Fact]
        public void OnAuthorization_AdminLevel_ValidHeader_Succeeds()
        {
            AuthorizationLevelAttribute attribute = new AuthorizationLevelAttribute(AuthorizationLevel.Admin);

            HttpRequestMessage request = new HttpRequestMessage();
            request.Headers.Add(AuthorizationLevelAttribute.MasterKeyHeaderName, "abc123");
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
                request.Headers.Add(AuthorizationLevelAttribute.MasterKeyHeaderName, headerValue);
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
            request.Headers.Add(AuthorizationLevelAttribute.MasterKeyHeaderName, TestMasterKeyValue);
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
    }
}
