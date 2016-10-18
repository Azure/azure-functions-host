// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Dependencies;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Handlers;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class WebScriptHostHandlerTests
    {
        private HttpMessageInvoker _invoker;
        private Mock<WebScriptHostManager> _managerMock;

        public WebScriptHostHandlerTests()
        {
            _managerMock = new Mock<WebScriptHostManager>(MockBehavior.Strict, new ScriptHostConfiguration(), new SecretManager(), new WebHostSettings());
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
            _managerMock.SetupGet(p => p.IsRunning).Returns(false);
            _managerMock.SetupGet(p => p.LastError).Returns((Exception)null);

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "http://functions.test.com/api/test");
            HttpResponseMessage response = await _invoker.SendAsync(request, CancellationToken.None);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }

        [Fact]
        public async Task SendAsync_HostRunning_ReturnsOk()
        {
            _managerMock.SetupGet(p => p.IsRunning).Returns(true);

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "http://functions.test.com/api/test");
            HttpResponseMessage response = await _invoker.SendAsync(request, CancellationToken.None);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task SendAsync_HostInErrorState_Returns503Immediately()
        {
            _managerMock.SetupGet(p => p.IsRunning).Returns(false);
            _managerMock.SetupGet(p => p.LastError).Returns(new Exception());

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "http://functions.test.com/api/test");
            HttpResponseMessage response = await _invoker.SendAsync(request, CancellationToken.None);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            _managerMock.VerifyGet(p => p.IsRunning, Times.Exactly(2));
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
