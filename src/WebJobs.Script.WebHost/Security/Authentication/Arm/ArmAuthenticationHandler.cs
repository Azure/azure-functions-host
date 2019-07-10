// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication
{
    public class ArmAuthenticationHandler : AuthenticationHandler<ArmAuthenticationOptions>
    {
        private readonly ILogger _logger;

        public ArmAuthenticationHandler(IOptionsMonitor<ArmAuthenticationOptions> options, ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock)
            : base(options, logger, encoder, clock)
        {
            _logger = logger.CreateLogger<ArmAuthenticationHandler>();
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            AuthenticateResult result = HandleAuthenticate();

            return Task.FromResult(result);
        }

        private AuthenticateResult HandleAuthenticate()
        {
            string token = null;
            if (!Context.Request.Headers.TryGetValue(ScriptConstants.SiteTokenHeaderName, out StringValues values))
            {
                return AuthenticateResult.NoResult();
            }

            token = values.First();

            try
            {
                if (!SimpleWebTokenHelper.ValidateToken(token, Clock))
                {
                    return AuthenticateResult.Fail("Token validation failed.");
                }

                var claims = new List<Claim>
                {
                    new Claim(SecurityConstants.AuthLevelClaimType, AuthorizationLevel.Admin.ToString())
                };

                var identity = new ClaimsIdentity(claims, ArmAuthenticationDefaults.AuthenticationScheme);
                return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
            }
            catch (Exception exc)
            {
                _logger.LogError(exc, "ARM authentication token validation failed.");
                return AuthenticateResult.Fail(exc);
            }
        }
    }
}
