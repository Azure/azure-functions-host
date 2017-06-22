// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
#if WEBHANDLERS
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
using Microsoft.Azure.WebJobs.Script.WebHost.Handlers;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Handlers
{
    public class SystemTraceHandlerTests
    {
        private readonly TestTraceWriter _traceWriter;
        private readonly HttpMessageInvoker _invoker;

        public SystemTraceHandlerTests()
        {
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
        }

        [Fact]
        public async Task SendAsync_WritesExpectedTraces()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "http://functions.com/api/testfunc?code=123");
            string requestId = Guid.NewGuid().ToString();
            request.Headers.Add(ScriptConstants.AntaresLogIdHeaderName, requestId);
            WebScriptHostHandler.SetRequestId(request);
            request.SetAuthorizationLevel(AuthorizationLevel.Function);

            await _invoker.SendAsync(request, CancellationToken.None);

            var traces = _traceWriter.Traces.ToArray();
            Assert.Equal(3, traces.Length);

            // validate executing trace
            var trace = traces[0];
            Assert.Equal(TraceLevel.Info, trace.Level);
            string message = Regex.Replace(trace.Message, @"\s+", string.Empty);
            Assert.Equal($"ExecutingHTTPrequest:{{\"requestId\":\"{requestId}\",\"method\":\"GET\",\"uri\":\"/api/testfunc\"}}", message);
            Assert.Equal(ScriptConstants.TraceSourceHttpHandler, trace.Source);

            // validate executed trace
            trace = traces[1];
            Assert.Equal(TraceLevel.Info, trace.Level);
            message = Regex.Replace(trace.Message, @"\s+", string.Empty);
            Assert.Equal($"ExecutedHTTPrequest:{{\"requestId\":\"{requestId}\",\"method\":\"GET\",\"uri\":\"/api/testfunc\",\"authorizationLevel\":\"Function\"}}", message);
            Assert.Equal(ScriptConstants.TraceSourceHttpHandler, trace.Source);

            // validate response trace
            trace = traces[2];
            Assert.Equal(TraceLevel.Info, trace.Level);
            message = Regex.Replace(trace.Message, @"\s+", string.Empty);
            Assert.Equal($"Responsedetails:{{\"requestId\":\"{requestId}\",\"status\":\"OK\"}}", message);
            Assert.Equal(ScriptConstants.TraceSourceHttpHandler, trace.Source);
        }
    }
}
#endif