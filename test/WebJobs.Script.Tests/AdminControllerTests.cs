// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Controllers;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class AdminControllerTests
    {
        [Fact]
        public void AdminController_HasAuthorizationLevelAttribute()
        {
            AuthorizationLevelAttribute attribute = typeof(AdminController).GetCustomAttribute<AuthorizationLevelAttribute>();
            Assert.Equal(AuthorizationLevel.Admin, attribute.Level);
        }

        [Fact]
        public async Task HostStatus_StillAccessible_WhenHostInstanceNull()
        {
            // create a test server pointing to an invalid host directory
            string scriptRoot = Path.Combine(Environment.CurrentDirectory, @"TestScripts\invalid");
            var httpServer = TestHelpers.CreateTestServer(scriptRoot);
            var httpClient = new HttpClient(httpServer);
            httpClient.BaseAddress = new Uri("https://localhost/");

            // initiate the first request which will cause the host to startup
            string uri = "admin/host/status";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add("x-functions-key", "t8laajal0a1ajkgzoqlfv5gxr4ebhqozebw4qzdy");
            HttpResponseMessage response = await httpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            HostStatus status = await response.Content.ReadAsAsync<HostStatus>();
            Assert.Equal("1.0.0.0", status.Version);

            // continue to poll the status endpoint until we receive the expected error
            await TestHelpers.Await(async () =>
            {
                await Task.Delay(1000);

                request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Add("x-functions-key", "t8laajal0a1ajkgzoqlfv5gxr4ebhqozebw4qzdy");
                response = await httpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                status = await response.Content.ReadAsAsync<HostStatus>();

                return status.Errors?.Count > 0;
            });

            Assert.True(status.Errors[0].StartsWith("Microsoft.Azure.WebJobs.Script: Unable to parse host.json file."));
        }
    }
}
