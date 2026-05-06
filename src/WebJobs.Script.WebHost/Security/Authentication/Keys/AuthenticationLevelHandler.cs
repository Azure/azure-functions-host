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
                    // The EasyAuth identity is materialized from a request header,
                    // so its claims are not host-issued. Strip claim types that
                    // affect host authorization decisions; those are owned by the
                    // host's own key and JWT handlers.
                    claimsIdentities.Add(SanitizeEasyAuthIdentity(easyAuthIdentity));
                }
            }

            if (requestAuthorizationLevel != AuthorizationLevel.Anonymous)
            {
                var claims = new List<Claim>
                {
                    new Claim(SecurityConstants.AuthLevelClaimType, requestAuthorizationLevel.ToString()),
                    new Claim(SecurityConstants.InvokeClaimType, "true")
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

        // Authorization-related claim types that must be sourced from a host
        // authentication handler (the key-based handler or the JWT/SWT
        // validator) rather than from the EasyAuth identity built off the
        // request header.
        private static readonly HashSet<string> _privilegedClaimTypes = new(StringComparer.Ordinal)
        {
            SecurityConstants.AuthLevelClaimType,
            SecurityConstants.AuthLevelKeyNameClaimType,
            SecurityConstants.InvokeClaimType,
            SecurityConstants.AssignUnencryptedClaimType,
        };

        internal static ClaimsIdentity SanitizeEasyAuthIdentity(ClaimsIdentity easyAuthIdentity)
        {
            IEnumerable<Claim> filteredClaims = easyAuthIdentity.Claims
                .Where(c => !_privilegedClaimTypes.Contains(c.Type));

            // Preserve NameClaimType / RoleClaimType so consumers reading the
            // user identity (name, roles) keep working; rebuild without the
            // host-authorization claim types.
            return new ClaimsIdentity(
                filteredClaims,
                easyAuthIdentity.AuthenticationType,
                easyAuthIdentity.NameClaimType,
                easyAuthIdentity.RoleClaimType);
        }

        internal static Task<(string KeyName, AuthorizationLevel Level)> GetAuthorizationKeyInfoAsync(HttpRequest request, ISecretManagerProvider secretManagerProvider)
        {
            if (secretManagerProvider.SecretsEnabled)
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
            }

            return Task.FromResult<(string, AuthorizationLevel)>((null, AuthorizationLevel.Anonymous));
        }
    }
}
