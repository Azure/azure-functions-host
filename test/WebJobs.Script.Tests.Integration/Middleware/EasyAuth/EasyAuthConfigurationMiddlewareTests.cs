// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Tests.Controllers;
using Microsoft.Azure.WebJobs.Script.Tests.Integration.Middleware.EasyAuth;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Middleware
{
    public class EasyAuthConfigurationMiddlewareTests : IClassFixture<EasyAuthConfigurationMiddlewareTests.Fixture>
    {
        public readonly Fixture _fixture;

        public EasyAuthConfigurationMiddlewareTests(Fixture fixture)
        {
            this._fixture = fixture;
        }

        [Fact]
        public Task Invoke_EasyAuthEnabled_InvokesNext()
        {
            var easyAuthSettings = new HostEasyAuthOptions
            {
                SiteAuthClientId = "id",
                SiteAuthEnabled = true
            };

            var easyAuthOptions = new OptionsWrapper<HostEasyAuthOptions>(easyAuthSettings);

            bool nextInvoked = false;
            RequestDelegate next = (ctxt) =>
            {
                nextInvoked = true;
                ctxt.Response.StatusCode = (int)HttpStatusCode.Accepted;
                return Task.CompletedTask;
            };

            // var middleware = new JobHostEasyAuthMiddleware(easyAuthOptions, new NullLogger<JobHostEasyAuthMiddleware>(), TestEnvironment.GetEnvironmentVariable());
            var httpContext = new DefaultHttpContext();
            //  await middleware.Invoke(httpContext, next);

            Assert.True(nextInvoked);
            return Task.CompletedTask;
        }

        [Fact]
        public void Invoke_EasyAuthEnabled_NoToken()
        {
            // enable easyauth
            // should return 401 unauthorized
            Assert.Equal(_fixture.HttpResponse.StatusCode.ToString(),"Unauthorized");

            // Assert.Equal("test easy auth", await response.Content.ReadAsStringAsync());
        }

        public class Fixture : EasyAuthScenarioTestFixture
        {
            private readonly string _requestUri = $"api/httpTrigger"; 

            public HttpResponseMessage HttpResponse { get; private set; }

            protected virtual string RequestUriFormat => _requestUri;

            public override async Task InitializeAsync()
            {
                await base.InitializeAsync();
                // add auth tokens to default request headers
                //HttpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("bearer", "token");
                // TODO - generate token


                // TODO - use httpclient to send request w/token. EA will unpack and create ClaimsPrincipal, etc. 
                //var easyAuthSettings = new HostEasyAuthOptions
                //{
                //    SiteAuthClientId = "id",
                //    SiteAuthEnabled = true,
                //};
                //var easyAuthOptions = new OptionsWrapper<HostEasyAuthOptions>(easyAuthSettings);
                HttpResponse = HttpClient.GetAsync(_requestUri).Result;

            }
        }
        // TODO - auth failure
    }
}
