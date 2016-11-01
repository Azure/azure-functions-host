// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Dependencies;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Handlers;
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

        public WebScriptHostHandlerTests()
        {
            _settingsManager = ScriptSettingsManager.Instance;
            _managerMock = new Mock<WebScriptHostManager>(MockBehavior.Strict, new ScriptHostConfiguration(), new TestSecretManagerFactory(), _settingsManager, new WebHostSettings { SecretsPath = _secretsDirectory.Path });
            _managerMock.SetupGet(p => p.Initialized).Returns(true);
            Mock<IDependencyResolver> mockResolver = new Mock<IDependencyResolver>(MockBehavior.Strict);
            mockResolver.Setup(p => p.GetService(typeof(WebScriptHostManager))).Returns(_managerMock.Object);

            HttpConfiguration config = new HttpConfiguration();
            config.DependencyResolver = mockResolver.Object;
            WebScriptHostHandler handler = new WebScriptHostHandler(config, hostTimeoutSeconds: 1, hostRunningPollIntervalMS: 50)
            {
                InnerHandler = new TestHandler()
            };
            _invoker = new HttpMessageInvoker(handler);
        }

        [Fact]
        public async Task SendAsync_HostNotRunning_Returns503()
        {
            _managerMock.SetupGet(p => p.State).Returns(ScriptHostState.Default);
            _managerMock.SetupGet(p => p.LastError).Returns((Exception)null);

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "http://functions.test.com/api/test");
            HttpResponseMessage response = await _invoker.SendAsync(request, CancellationToken.None);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }

        [Fact]
        public async Task SendAsync_HostRunning_ReturnsOk()
        {
            _managerMock.SetupGet(p => p.State).Returns(ScriptHostState.Running);

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "http://functions.test.com/api/test");
            HttpResponseMessage response = await _invoker.SendAsync(request, CancellationToken.None);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task SendAsync_HostInErrorState_Returns503Immediately()
        {
            _managerMock.SetupGet(p => p.State).Returns(ScriptHostState.Error);
            _managerMock.SetupGet(p => p.LastError).Returns(new Exception());

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "http://functions.test.com/api/test");
            HttpResponseMessage response = await _invoker.SendAsync(request, CancellationToken.None);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            _managerMock.VerifyGet(p => p.State, Times.Exactly(5));
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

        public class TestHandler : DelegatingHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.Factory.StartNew(() => new HttpResponseMessage(HttpStatusCode.OK), cancellationToken);
            }
        }
    }
}
