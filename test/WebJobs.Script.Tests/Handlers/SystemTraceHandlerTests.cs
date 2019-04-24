// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Dependencies;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Handlers;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Handlers
{
    public class SystemTraceHandlerTests : IDisposable
    {
        private const string TestHostName = "test.azurewebsites.net";

        private readonly TestTraceWriter _traceWriter;
        private readonly HttpMessageInvoker _invoker;
        private readonly Mock<ScriptSettingsManager> _mockSettings;

        public SystemTraceHandlerTests()
        {
            _mockSettings = new Mock<ScriptSettingsManager>(MockBehavior.Strict);
            _mockSettings.Setup(p => p.GetSetting(EnvironmentSettingNames.AzureWebsiteHostName)).Returns(TestHostName);
            ScriptSettingsManager.Instance = _mockSettings.Object;

            _traceWriter = new TestTraceWriter(TraceLevel.Verbose);
            var config = new System.Web.Http.HttpConfiguration();
            Mock<IDependencyResolver> mockResolver = new Mock<IDependencyResolver>(MockBehavior.Strict);
            mockResolver.Setup(p => p.GetService(typeof(TraceWriter))).Returns(_traceWriter);
            config.DependencyResolver = mockResolver.Object;

            var handler = new SystemTraceHandler(config)
            {
                InnerHandler = new TestHandler()
            };
            _invoker = new HttpMessageInvoker(handler);

            HostNameProvider.Reset();
        }

        [Fact]
        public async Task SendAsync_WritesExpectedTraces()
        {
            Assert.Equal(TestHostName, HostNameProvider.Value);

            var request = new HttpRequestMessage(HttpMethod.Get, "http://functions.com/api/testfunc?code=123");
            string requestId = Guid.NewGuid().ToString();
            request.Headers.Add(ScriptConstants.AntaresLogIdHeaderName, requestId);
            request.Headers.Add(ScriptConstants.AntaresDefaultHostNameHeader, "test2.azurewebsites.net");
            SystemTraceHandler.SetRequestId(request);
            request.SetAuthorizationLevel(AuthorizationLevel.Function);

            await _invoker.SendAsync(request, CancellationToken.None);

            var traces = _traceWriter.GetTraces().ToArray();
            Assert.Equal(3, traces.Length);

            // validate hostname sync trace
            var trace = traces[0];
            var message = trace.Message;
            Assert.Equal(TraceLevel.Info, trace.Level);
            Assert.Equal("HostName updated from 'test.azurewebsites.net' to 'test2.azurewebsites.net'", message);
            Assert.Equal(ScriptConstants.TraceSourceHttpHandler, trace.Source);

            // verify the hostname was synchronized
            Assert.Equal("test2.azurewebsites.net", HostNameProvider.Value);

            // validate executing trace
            trace = traces[1];
            Assert.Equal(TraceLevel.Info, trace.Level);
            message = Regex.Replace(trace.Message, @"\s+", string.Empty);
            Assert.Equal($"ExecutingHTTPrequest:{{\"requestId\":\"{requestId}\",\"method\":\"GET\",\"uri\":\"/api/testfunc\"}}", message);
            Assert.Equal(ScriptConstants.TraceSourceHttpHandler, trace.Source);

            // validate executed trace
            trace = traces[2];
            Assert.Equal(TraceLevel.Info, trace.Level);
            message = Regex.Replace(trace.Message, @"\s+", string.Empty);
            Assert.Equal($"ExecutedHTTPrequest:{{\"requestId\":\"{requestId}\",\"method\":\"GET\",\"uri\":\"/api/testfunc\",\"authorizationLevel\":\"Function\",\"status\":\"OK\"}}", message);
            Assert.Equal(ScriptConstants.TraceSourceHttpHandler, trace.Source);
        }

        [Fact]
        public void SetRequestId_SetsExpectedValue()
        {
            // if the log header is present, it is used;
            var request = new HttpRequestMessage(HttpMethod.Get, "http://test.com");
            string logIdValue = Guid.NewGuid().ToString();
            request.Headers.Add(ScriptConstants.AntaresLogIdHeaderName, logIdValue);
            SystemTraceHandler.SetRequestId(request);
            string requestId = request.GetRequestId();
            Assert.Equal(logIdValue, requestId);

            // otherwise a new guid is specified
            request = new HttpRequestMessage(HttpMethod.Get, "http://test.com");
            SystemTraceHandler.SetRequestId(request);
            requestId = request.GetRequestId();
            Guid.Parse(requestId);
        }

        public void Dispose()
        {
            ScriptSettingsManager.Instance = new ScriptSettingsManager();
        }
    }
}
