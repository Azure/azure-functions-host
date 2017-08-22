// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.AppService.Proxy.Client.Contract;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.WebJobs.Script.Tests;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.Binding;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ProxyEndToEndTests : EndToEndTestsBase<ProxyEndToEndTests.TestFixture>
    {
        public ProxyEndToEndTests(TestFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task Proxy_Invoke_Succeeds()
        {
            HttpRequest request = HttpTestHelpers.CreateHttpRequest("GET", "http://localhost/mymockhttp");
            
            //request.SetConfiguration(Fixture.RequestConfiguration);

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request },
            };
            await Fixture.Host.CallAsync("localFunction", arguments);

            var response = (RawScriptResult)request.HttpContext.Items[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
            Assert.Equal("123", response.Headers["myversion"].ToString());
        }

        public class TestFixture : EndToEndTestFixture
        {
            private const string ScriptRoot = @"TestScripts\Proxies";

            public TestFixture() : base(ScriptRoot, "proxy", GetMockProxyClient())
            {
            }

            /// <summary>
            /// This method creates a mock proxy client that emulates the behaviour of Azure Function Proxies for
            /// TestScripts\Proxies\proxies.json
            /// </summary>
            /// <returns>Mock IProxyClient object</returns>
            private static ProxyClientExecutor GetMockProxyClient()
            {
                var proxyClient = new Mock<IProxyClient>();

                ProxyData proxyData = new ProxyData();
                proxyData.Routes.Add(new Routes()
                {
                    Methods = new[] { HttpMethod.Get, HttpMethod.Post },
                    Name = "test",
                    UrlTemplate = "/myproxy"
                });

                proxyData.Routes.Add(new Routes()
                {
                    Methods = new[] { HttpMethod.Get },
                    Name = "localFunction",
                    UrlTemplate = "/mymockhttp"
                });

                proxyClient.Setup(p => p.GetProxyData()).Returns(proxyData);

                proxyClient.Setup(p => p.CallAsync(It.IsAny<object[]>(), It.IsAny<IFuncExecutor>(), It.IsAny<ILogger>())).Returns(
                    (object[] arguments, IFuncExecutor funcExecutor, ILogger logger) =>
                    {
                        object requestObj = arguments != null && arguments.Length > 0 ? arguments[0] : null;
                        var request = requestObj as HttpRequest;
                        if (string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(request.GetDisplayUrl(), "http://localhost/mymockhttp", StringComparison.OrdinalIgnoreCase))
                        {
                            var response = new RawScriptResult(StatusCodes.Status200OK, null)
                            {
                                Headers = new Dictionary<string, object>
                                {
                                    { "myversion", "123" }
                                }
                            };
                            
                            request.HttpContext.Items[ScriptConstants.AzureFunctionsHttpResponseKey] = response;
                        }
                        return Task.CompletedTask;
                    });

                return new ProxyClientExecutor(proxyClient.Object);
            }
        }
    }
}
