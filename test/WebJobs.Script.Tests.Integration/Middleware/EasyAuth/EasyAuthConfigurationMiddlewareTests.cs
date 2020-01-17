// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Tests.Controllers;
using Microsoft.Azure.WebJobs.Script.Tests.Integration.Middleware.EasyAuth;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Middleware
{
    public class EasyAuthConfigurationMiddlewareTests
    {
        ControllerScenarioTestFixture _fixture;

        public EasyAuthConfigurationMiddlewareTests(EasyAuthScenarioTestFixture fixture)
        {
            this._fixture = fixture;
        }

        [Fact]
        public async Task Invoke_EasyAuthEnabled_InvokesNext()
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

            var middleware = new JobHostEasyAuthMiddleware(easyAuthOptions, null);
            var httpContext = new DefaultHttpContext();
            await middleware.Invoke(httpContext, next);

            Assert.True(nextInvoked);
        }

        [Fact]
        public async Task Invoke_EasyAuthEnabled()
        {
            // enable easyauth
            // should return 401 unauthorized
            var envVars = new Dictionary<string, string>()
            {
                { EnvironmentSettingNames.EasyAuthClientId, "23jekfs" },
                { EnvironmentSettingNames.EasyAuthEnabled, "true" },
                { EnvironmentSettingNames.ContainerName, "linuxconsumption" },
                { EnvironmentSettingNames.EasyAuthSigningKey, "2892B532EB2C17AC3DD2009CBBF9C9CA7A3F9189FA4241789A4E26DE859077C0" },
                { EnvironmentSettingNames.EasyAuthEncryptionKey, "723249EF012A5FCE5946F65FBE7D6CB209331612E651B638C2F46BF9DB39F530" }
            };
            var testEnv = new TestEnvironment(envVars);
            var easyAuthSettings = new HostEasyAuthOptions
            {
                SiteAuthClientId = "id",
                SiteAuthEnabled = true,
                // TODO - JHEAMiddleware construction needs config value from options. Should this be the case?
            };

            var easyAuthOptions = new OptionsWrapper<HostEasyAuthOptions>(easyAuthSettings);
            bool nextInvoked = false;

            RequestDelegate next = (ctxt) =>
            {
                nextInvoked = true;
                ctxt.Response.StatusCode = (int)HttpStatusCode.Accepted;
                return Task.CompletedTask;
            };

            // todo - need?
            var claims = new List<Claim>
                {
                    new Claim(SecurityConstants.AuthLevelClaimType, AuthorizationLevel.Function.ToString())
                };
            var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

            var middleware = new JobHostEasyAuthMiddleware(easyAuthOptions, new NullLogger<JobHostEasyAuthMiddleware>());
            var httpContext = new DefaultHttpContext();
            await middleware.Invoke(httpContext, next);

            Assert.False(nextInvoked);

            // var response = await client.GetAsync(string.Empty);
            //  Assert.Equal(response.StatusCode.ToString(), "401");
            // Assert.Equal("test easy auth", await response.Content.ReadAsStringAsync());
        }

        public class Fixture : EasyAuthScenarioTestFixture
        {
            private string _requestUri = "https://localhost/";

            public HttpResponseMessage HttpResponse { get; private set; }

            protected virtual string RequestUriFormat => _requestUri;

            public override async Task InitializeAsync()
            {
                await base.InitializeAsync();
                // add auth tokens to default request headers
                HttpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("bearer", "token");
            }
        }
        // TODO - auth failure
    }
}
