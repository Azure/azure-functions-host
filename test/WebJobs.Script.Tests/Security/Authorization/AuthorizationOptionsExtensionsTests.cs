// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Controllers;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Security.Authorization
{
    public class AuthorizationOptionsExtensionsTests
    {
        [Theory]
        // The fix: the SyncTriggers action, with isolation enabled and the request routed through the
        // Front End, is allowed when the caller presents an SCM (Kudu) site token.
        [InlineData(nameof(HostController.SyncTriggers), true, true, false, true)]
        // Caller scoping: the same SyncTriggers request without an SCM token remains blocked under isolation.
        [InlineData(nameof(HostController.SyncTriggers), true, false, false, false)]
        // Endpoint scoping: a different admin action is still blocked under isolation even with an SCM token.
        [InlineData(nameof(HostController.GetHostStatus), true, true, false, false)]
        // With isolation disabled, the assertion always allows the request.
        [InlineData(nameof(HostController.SyncTriggers), false, false, false, true)]
        // Existing allowance is unaffected: an AppService-internal request (Front End bypassed) is allowed.
        [InlineData(nameof(HostController.SyncTriggers), true, false, true, true)]
        [InlineData(nameof(HostController.GetHostStatus), true, false, true, true)]
        public async Task AdminAuthLevelPolicy_SyncTriggersExemption_ScopedToScmCaller(string actionName, bool isolationEnabled, bool isScmToken, bool bypassFrontEnd, bool expectSuccess)
        {
            AssertionRequirement assertion = GetAdminAuthLevelAssertion();

            var user = CreateUser(isScmToken);
            AuthorizationFilterContext filterContext = CreateFilterContext(actionName, isolationEnabled, bypassFrontEnd, user);
            var context = new AuthorizationHandlerContext(new[] { assertion }, user, filterContext);

            await assertion.HandleAsync(context);

            Assert.Equal(expectSuccess, context.HasSucceeded);
        }

        private static AssertionRequirement GetAdminAuthLevelAssertion()
        {
            var options = new AuthorizationOptions();
            options.AddScriptPolicies();

            AuthorizationPolicy policy = options.GetPolicy(PolicyNames.AdminAuthLevel);
            return policy.Requirements.OfType<AssertionRequirement>().Single();
        }

        private static ClaimsPrincipal CreateUser(bool isScmToken)
        {
            var claims = new List<Claim>
            {
                new Claim(SecurityConstants.AuthLevelClaimType, AuthorizationLevel.Admin.ToString())
            };

            if (isScmToken)
            {
                claims.Add(new Claim(SecurityConstants.ScmSiteTokenClaimType, "true"));
            }

            return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        }

        private static AuthorizationFilterContext CreateFilterContext(string actionName, bool isolationEnabled, bool bypassFrontEnd, ClaimsPrincipal user)
        {
            var environment = new TestEnvironment();

            // Populating the instance id makes the host treat itself as App Service, which ensures all
            // requests are expected to flow through the Front End (so admin isolation is enforced).
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId, "1");

            if (isolationEnabled)
            {
                environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsAdminIsolationEnabled, "1");
            }

            var services = new ServiceCollection();
            services.AddSingleton<IEnvironment>(environment);

            var httpContext = new DefaultHttpContext
            {
                RequestServices = services.BuildServiceProvider(),
                User = user
            };

            if (!bypassFrontEnd)
            {
                // The presence of this header indicates the request was routed through the Front End,
                // i.e. it is not an internal (Front End bypassing) request.
                httpContext.Request.Headers[ScriptConstants.AntaresLogIdHeaderName] = Guid.NewGuid().ToString();
            }

            var method = typeof(HostController).GetMethod(actionName)
                 ?? throw new InvalidOperationException($"Action '{actionName}' not found on {nameof(HostController)}.");

            var actionDescriptor = new ControllerActionDescriptor
            {
                MethodInfo = method
            };

            var actionContext = new ActionContext(httpContext, new RouteData(), actionDescriptor);
            return new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());
        }
    }
}
