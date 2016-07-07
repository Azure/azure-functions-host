// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.


using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Newtonsoft.Json;
using SuaveServerWrapper;
using WebJobs.Script.Cli.Extensions;
using Xunit;

namespace WebJobs.Script.Cli.Tests
{
    public class UriExtensionsTests
    {
        [Theory]
        [InlineData("https://example.com", false)]
        [InlineData("invalid://example.com", false)]
        public async Task IsServerRunningNegativeTest(string Url, bool expected)
        {
            var uri = new Uri(Url);
            var result = await uri.IsServerRunningAsync();
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("{\"test\": \"file\"}", true)]
        [InlineData("test", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsJsonTest(string value, bool expected)
        {
            Assert.Equal(expected, value.IsJson());
        }

        [Fact]
        public async Task IsServerRunningPositiveTest()
        {
            var port = GetAvailablePort();
            using (var httpHost = new HttpHost(port))
            {
                await httpHost.OpenAsync(r => {
                    var url = r.RequestUri;
                    HttpResponseMessage response;

                    if (url.AbsolutePath == "/")
                    {
                        response = new HttpResponseMessage(HttpStatusCode.NoContent);
                    }
                    else if (url.AbsolutePath.Equals("/admin/host/status", StringComparison.OrdinalIgnoreCase))
                    {
                        response = new HttpResponseMessage
                        {
                            Content = new StringContent(JsonConvert.SerializeObject(new HostStatus { WebHostSettings = new WebHostSettings() }), Encoding.UTF8, "application/json")
                        };
                    }
                    else
                    {
                        response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
                    }

                    return Task.FromResult(response);
                });

                var uri = new Uri($"http://localhost:{port}");
                var result = await uri.IsServerRunningAsync();

                result.Should().BeTrue(because: "Server is running");

                httpHost.Close();
            }
        }

        private static int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start();
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
