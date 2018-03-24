// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#if WEBHANDLERS
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Handlers;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class WebScriptHostHandlerTests : IDisposable
    {
        private readonly ScriptSettingsManager _settingsManager;
        private readonly TempDirectory _secretsDirectory = new TempDirectory();
        private HttpMessageInvoker _invoker;
        private Mock<WebScriptHostManager> _managerMock;
        private WebHostSettings _webHostSettings;

        public WebScriptHostHandlerTests()
        {
            _settingsManager = ScriptSettingsManager.Instance;
            var eventManager = new Mock<IScriptEventManager>();
            _managerMock = new Mock<WebScriptHostManager>(MockBehavior.Strict, new ScriptHostConfiguration(), new TestSecretManagerFactory(), eventManager.Object,
                _settingsManager, new WebHostSettings { SecretsPath = _secretsDirectory.Path }, null, null, null, 1, 50);

            _managerMock.SetupGet(p => p.Initialized).Returns(true);
            Mock<IDependencyResolver> mockResolver = new Mock<IDependencyResolver>(MockBehavior.Strict);
            mockResolver.Setup(p => p.GetService(typeof(WebScriptHostManager))).Returns(_managerMock.Object);

            _webHostSettings = new WebHostSettings();
            mockResolver.Setup(p => p.GetService(typeof(WebHostSettings))).Returns(_webHostSettings);

            HttpConfiguration config = new HttpConfiguration();
            config.DependencyResolver = mockResolver.Object;
            WebScriptHostHandler handler = new WebScriptHostHandler(config)
            {
                InnerHandler = new TestHandler()
            };
            _invoker = new HttpMessageInvoker(handler);
        }

        [Fact]
        public void SetRequestId_SetsExpectedValue()
        {
            // if the log header is present, it is used;
            var request = new HttpRequestMessage(HttpMethod.Get, "http://test.com");
            string logIdValue = Guid.NewGuid().ToString();
            request.Headers.Add(ScriptConstants.AntaresLogIdHeaderName, logIdValue);
            WebScriptHostHandler.SetRequestId(request);
            string requestId = request.GetRequestId();
            Assert.Equal(logIdValue, requestId);

            // otherwise a new guid is specified
            request = new HttpRequestMessage(HttpMethod.Get, "http://test.com");
            WebScriptHostHandler.SetRequestId(request);
            requestId = request.GetRequestId();
            Guid.Parse(requestId);
        }

        [Fact]
        public async Task SendAsync_AuthDisabled_SetsExpectedRequestProperty()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "http://functions.test.com/admin/host/status");
            var response = await _invoker.SendAsync(request, CancellationToken.None);
            Assert.False(request.IsAuthDisabled());

            _webHostSettings.IsAuthDisabled = true;
            request = new HttpRequestMessage(HttpMethod.Get, "http://functions.test.com/admin/host/status");
            response = await _invoker.SendAsync(request, CancellationToken.None);
            Assert.True(request.IsAuthDisabled());
        }

        [Fact]
        public async Task SendAsync_HostRunning_ReturnsOk()
        {
            _managerMock.SetupGet(p => p.State).Returns(ScriptHostState.Running);

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "http://functions.test.com/api/test");
            HttpResponseMessage response = await _invoker.SendAsync(request, CancellationToken.None);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _secretsDirectory.Dispose();
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
    }
}
#endif