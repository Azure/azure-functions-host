// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Yarp.ReverseProxy.Forwarder;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class DefaultHttpProxyServiceTests
    {
        private readonly Mock<IHttpForwarder> _httpForwarderMock;
        private readonly Mock<ILogger<DefaultHttpProxyService>> _loggerMock;
        private readonly DefaultHttpProxyService _proxyService;

        public DefaultHttpProxyServiceTests()
        {
            _httpForwarderMock = new Mock<IHttpForwarder>();
            _loggerMock = new Mock<ILogger<DefaultHttpProxyService>>();
            _proxyService = new DefaultHttpProxyService(_httpForwarderMock.Object, _loggerMock.Object);
        }

        [Fact]
        public void StartForwarding_SetsCorrelationHeader()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Items.Add(ScriptConstants.AzureFunctionsHttpTriggerContext, new());
            var invocationId = Guid.NewGuid();
            var context = new ScriptInvocationContext
            {
                FunctionMetadata = new FunctionMetadata { Name = "TestFunction" },
                ExecutionContext = new ExecutionContext { InvocationId = invocationId },
                Inputs = new List<(string Name, DataType Type, object Val)>
                {
                    ("req", DataType.String, httpContext.Request)
                },
                Properties = new Dictionary<string, object>()
            };

            var httpUri = new Uri("http://localhost");

            _proxyService.StartForwarding(context, httpUri);

            var httpRequest = (HttpRequest)context.Inputs.First().Val;
            Assert.True(httpRequest.Headers.ContainsKey(ScriptConstants.HttpProxyCorrelationHeader));
            Assert.Equal(invocationId.ToString(), httpRequest.Headers[ScriptConstants.HttpProxyCorrelationHeader]);
        }

        [Fact]
        public void StartForwarding_OverridesExistingCorrelationHeader()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Items.Add(ScriptConstants.AzureFunctionsHttpTriggerContext, new());
            var invocationId = Guid.NewGuid();
            var context = new ScriptInvocationContext
            {
                FunctionMetadata = new FunctionMetadata { Name = "TestFunction" },
                ExecutionContext = new ExecutionContext { InvocationId = invocationId },
                Inputs = new List<(string Name, DataType Type, object Val)>
                {
                    ("req", DataType.String, httpContext.Request)
                },
                Properties = new Dictionary<string, object>()
            };

            var httpUri = new Uri("http://localhost");
            var existingCorrelationId = Guid.NewGuid().ToString();
            var httpRequest = (HttpRequest)context.Inputs.First().Val;
            httpRequest.Headers[ScriptConstants.HttpProxyCorrelationHeader] = existingCorrelationId;

            _proxyService.StartForwarding(context, httpUri);
            Assert.Equal(invocationId.ToString(), httpRequest.Headers[ScriptConstants.HttpProxyCorrelationHeader]);
        }
    }
}