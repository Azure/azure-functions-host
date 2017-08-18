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
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/mymockhttp")),
                Method = HttpMethod.Get,
            };

            request.SetConfiguration(Fixture.RequestConfiguration);

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request },
            };
            await Fixture.Host.CallAsync("localFunction", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(response.Headers.GetValues("myversion").ToArray()[0], "123");
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
                        var request = requestObj as HttpRequestMessage;
                        if (request.Method == HttpMethod.Get && request.RequestUri.OriginalString == "http://localhost/mymockhttp")
                        {
                            var response = new HttpResponseMessage(HttpStatusCode.OK);
                            response.Headers.Add("myversion", "123");
                            request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey] = response;
                        }
                        return Task.CompletedTask;
                    });

                return new ProxyClientExecutor(proxyClient.Object);
            }
        }
    }
}
