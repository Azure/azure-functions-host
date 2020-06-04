// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Features;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Authentication
{
    internal class AuthenticationLevelHandler : AuthenticationHandler<AuthenticationLevelOptions>
    {
        public const string FunctionsKeyHeaderName = "x-functions-key";
        public const string FunctionsKeyQueryParamName = "code";
        private readonly ISecretManagerProvider _secretManagerProvider;
        private readonly bool _isEasyAuthEnabled;

        public AuthenticationLevelHandler(
            IOptionsMonitor<AuthenticationLevelOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IDataProtectionProvider dataProtection,
            ISystemClock clock,
            ISecretManagerProvider secretManagerProvider,
            IEnvironment environment)
            : base(options, logger, encoder, clock)
        {
            _secretManagerProvider = secretManagerProvider;
            _isEasyAuthEnabled = environment.IsEasyAuthEnabled();
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Get the authorization level for the current request
            (string name, AuthorizationLevel requestAuthorizationLevel) = await GetAuthorizationKeyInfoAsync(Context.Request, _secretManagerProvider);

            List<ClaimsIdentity> claimsIdentities = new List<ClaimsIdentity>();

            if (_isEasyAuthEnabled)
            {
                ClaimsIdentity easyAuthIdentity = Context.Request.GetAppServiceIdentity();
                if (easyAuthIdentity != null)
                {
                    claimsIdentities.Add(easyAuthIdentity);
                }
            }

            if (requestAuthorizationLevel != AuthorizationLevel.Anonymous)
            {
                var claims = new List<Claim>
                {
                    new Claim(SecurityConstants.AuthLevelClaimType, requestAuthorizationLevel.ToString())
                };

                if (!string.IsNullOrEmpty(name))
                {
                    claims.Add(new Claim(SecurityConstants.AuthLevelKeyNameClaimType, name));
                }

                var keyIdentity = new ClaimsIdentity(claims, AuthLevelAuthenticationDefaults.AuthenticationScheme);
                claimsIdentities.Add(keyIdentity);
            }

            if (claimsIdentities.Count > 0)
            {
                return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(claimsIdentities), Scheme.Name));
            }
            else
            {
                return AuthenticateResult.NoResult();
            }
        }

        internal static Task<(string, AuthorizationLevel)> GetAuthorizationKeyInfoAsync(HttpRequest request, ISecretManagerProvider secretManagerProvider)
        {
            // first see if a key value is specified via headers or query string (header takes precedence)
            string keyValue = null;
            if (request.Headers.TryGetValue(FunctionsKeyHeaderName, out StringValues values))
            {
                keyValue = values.First();
            }
            else if (request.Query.TryGetValue(FunctionsKeyQueryParamName, out values))
            {
                keyValue = values.First();
            }

            if (!string.IsNullOrEmpty(keyValue))
            {
                ISecretManager secretManager = secretManagerProvider.Current;
                var functionName = request.HttpContext.Features.Get<IFunctionExecutionFeature>()?.Descriptor.Name;
                return secretManager.GetAuthorizationLevelOrNullAsync(keyValue, functionName);
            }

            return Task.FromResult<(string, AuthorizationLevel)>((null, AuthorizationLevel.Anonymous));
        }
    }
}
