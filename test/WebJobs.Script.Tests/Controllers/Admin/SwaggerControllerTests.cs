// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#if SWAGGER
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Http.Results;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Controllers;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Moq;
using Newtonsoft.Json.Linq;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Controllers.Admin
{
    public class SwaggerControllerTests
    {
        private readonly ScriptSettingsManager _settingsManager;
        private readonly TempDirectory _secretsDirectory = new TempDirectory();
        private Mock<ScriptHost> _hostMock;
        private Mock<WebScriptHostManager> _managerMock;
        private Collection<FunctionDescriptor> _testFunctions;
        private SwaggerController _testController;
        private Mock<ISwaggerDocumentManager> _swaggerDocumentManagerMock;
        private ScriptHostConfiguration _config = new ScriptHostConfiguration();

        public SwaggerControllerTests()
        {
            _settingsManager = ScriptSettingsManager.Instance;
            _testFunctions = new Collection<FunctionDescriptor>();
            var environment = new NullScriptHostEnvironment();
            var eventManager = new Mock<IScriptEventManager>();
            _hostMock = new Mock<ScriptHost>(MockBehavior.Strict, new object[] { environment, eventManager.Object, _config, null, null });
            _hostMock.Setup(p => p.Functions).Returns(_testFunctions);
            _hostMock.Object.ScriptConfig.SwaggerEnabled = true;

            WebHostSettings settings = new WebHostSettings();
            settings.SecretsPath = _secretsDirectory.Path;
            _managerMock = new Mock<WebScriptHostManager>(MockBehavior.Strict, new object[] { _config, new TestSecretManagerFactory(), eventManager.Object, _settingsManager, settings });
            _managerMock.SetupGet(p => p.Instance).Returns(_hostMock.Object);
            _swaggerDocumentManagerMock = new Mock<ISwaggerDocumentManager>(MockBehavior.Strict);
            var traceWriter = new TestTraceWriter(TraceLevel.Verbose);
            _testController = new SwaggerController(_swaggerDocumentManagerMock.Object, _managerMock.Object, traceWriter, null);
        }

        [Fact]
        public void HasAuthorizationLevelAttribute()
        {
            var attribute = typeof(SwaggerController).GetCustomAttribute<AuthorizationLevelAttribute>();
            Assert.Equal(AuthorizationLevel.System, attribute.Level);
            Assert.Equal(ScriptConstants.SwaggerDocumentationKey, attribute.KeyName);
        }

        [Fact]
        public void GetGeneratedSwaggerDocument_ReturnsSwaggerDocument()
        {
            JObject json = new JObject();
            _testController.Request = new HttpRequestMessage(HttpMethod.Get, "https://local/admin/host/swagger/default");
            _swaggerDocumentManagerMock.Setup(p => p.GenerateSwaggerDocument(null)).Returns(json);
            var result = (OkNegotiatedContentResult<JObject>)_testController.GetGeneratedSwaggerDocument();
            Assert.Equal(json, result.Content);
        }

        [Fact]
        public void GetGeneratedSwaggerDocument_ReturnsInternalServerError()
        {
            _testController.Request = new HttpRequestMessage(HttpMethod.Get, "https://local/admin/host/swagger/default");
            _swaggerDocumentManagerMock.Setup(p => p.GenerateSwaggerDocument(null)).Throws(new Exception("TestException"));
            Exception result = Assert.Throws<Exception>(() => _testController.GetGeneratedSwaggerDocument());
            Assert.Equal("TestException", result.Message);
        }

        [Fact]
        public async Task GetSwaggerDocumentAsync_ReturnsSwaggerDocument()
        {
            JObject json = new JObject();
            _testController.Request = new HttpRequestMessage(HttpMethod.Get, "https://local/admin/host/swagger/default");
            _swaggerDocumentManagerMock.Setup(p => p.GetSwaggerDocumentAsync()).ReturnsAsync(json);
            var result = (OkNegotiatedContentResult<JObject>)await _testController.GetSwaggerDocumentAsync();
            Assert.Equal(json, result.Content);
        }

        [Fact]
        public async Task GetSwaggerDocumentAsync_ReturnsInternalServerError()
        {
            JObject json = new JObject();
            _testController.Request = new HttpRequestMessage(HttpMethod.Get, "https://local/admin/host/swagger/default");
            _swaggerDocumentManagerMock.Setup(p => p.GetSwaggerDocumentAsync()).Throws(new Exception("TestException"));
            Exception result = await Assert.ThrowsAsync<Exception>(() => _testController.GetSwaggerDocumentAsync());
            Assert.Equal("TestException", result.Message);
        }

        [Fact]
        public async Task GetSwaggerDocumentAsync_ReturnsNotFound_WhenDocumentNotPresent()
        {
            JObject json = null;
            _testController.Request = new HttpRequestMessage(HttpMethod.Get, "https://local/admin/host/swagger/default");
            _swaggerDocumentManagerMock.Setup(p => p.GetSwaggerDocumentAsync()).ReturnsAsync(json);
            var result = (NotFoundResult)await _testController.GetSwaggerDocumentAsync();
            Assert.IsAssignableFrom(typeof(NotFoundResult), result);
        }

        [Fact]
        public async Task GetSwaggerDocumentAsync_ReturnsNotFound_WhenDisabled()
        {
            _hostMock.Object.ScriptConfig.SwaggerEnabled = false;
            JObject json = new JObject();
            _testController.Request = new HttpRequestMessage(HttpMethod.Get, "https://local/admin/host/swagger/default");
            _swaggerDocumentManagerMock.Setup(p => p.GetSwaggerDocumentAsync()).ReturnsAsync(json);
            var result = (NotFoundResult)await _testController.GetSwaggerDocumentAsync();
            Assert.IsAssignableFrom(typeof(NotFoundResult), result);
        }

        [Fact]
        public async Task AddOrUpdateSwaggerDocumentAsync_ReturnsSwaggerDocument()
        {
            JObject json = null;
            _testController.Request = new HttpRequestMessage(HttpMethod.Post, "https://local/admin/host/swagger/default");
            _swaggerDocumentManagerMock.Setup(p => p.AddOrUpdateSwaggerDocumentAsync(json)).ReturnsAsync(json);
            var result = (OkNegotiatedContentResult<JObject>)await _testController.AddOrUpdateSwaggerDocumentAsync(json);
            Assert.Equal(json, result.Content);
        }

        [Fact]
        public async Task AddOrUpdateSwaggerDocumentAsync_ReturnsInternalServerError()
        {
            JObject json = new JObject();
            _testController.Request = new HttpRequestMessage(HttpMethod.Post, "https://local/admin/host/swagger/default");
            _swaggerDocumentManagerMock.Setup(p => p.AddOrUpdateSwaggerDocumentAsync(It.IsAny<JObject>())).Throws(new Exception("TestException"));
            Exception result = await Assert.ThrowsAsync<Exception>(() => _testController.AddOrUpdateSwaggerDocumentAsync(json));
            Assert.Equal("TestException", result.Message);
        }

        [Fact]
        public async Task DeleteSwaggerDocumentAsync_ReturnsNoContent()
        {
            _testController.Request = new HttpRequestMessage(HttpMethod.Delete, "https://local/admin/host/swagger/default");
            _swaggerDocumentManagerMock.Setup(p => p.DeleteSwaggerDocumentAsync()).ReturnsAsync(true);
            var result = (StatusCodeResult)await _testController.DeleteSwaggerDocumentAsync();
            Assert.Equal(HttpStatusCode.NoContent, result.StatusCode);
        }

        [Fact]
        public async Task DeleteSwaggerDocumentAsync_ReturnsNoFound()
        {
            _testController.Request = new HttpRequestMessage(HttpMethod.Delete, "https://local/admin/host/swagger/default");
            _swaggerDocumentManagerMock.Setup(p => p.DeleteSwaggerDocumentAsync()).ReturnsAsync(false);
            var result = (NotFoundResult)await _testController.DeleteSwaggerDocumentAsync();
            Assert.IsAssignableFrom(typeof(NotFoundResult), result);
        }

        [Fact]
        public async Task DeleteSwaggerDocumentAsync_ReturnsInternalServerError()
        {
            JObject json = new JObject();
            _testController.Request = new HttpRequestMessage(HttpMethod.Delete, "https://local/admin/host/swagger/default");
            _swaggerDocumentManagerMock.Setup(p => p.DeleteSwaggerDocumentAsync()).Throws(new Exception("TestException"));
            Exception result = await Assert.ThrowsAsync<Exception>(() => _testController.DeleteSwaggerDocumentAsync());
            Assert.Equal("TestException", result.Message);
        }
    }
}
#endif