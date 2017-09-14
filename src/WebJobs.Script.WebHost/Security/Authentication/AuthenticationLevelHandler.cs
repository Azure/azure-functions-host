// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Authentication
{
    internal class AuthenticationLevelHandler : AuthenticationHandler<AuthenticationLevelOptions>
    {
        public const string FunctionsKeyHeaderName = "x-functions-key";
        private readonly ISecretManager _secretManager;

        public AuthenticationLevelHandler(
            IOptionsMonitor<AuthenticationLevelOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IDataProtectionProvider dataProtection,
            ISystemClock clock,
            ISecretManager secretManager)
            : base(options, logger, encoder, clock)
        {
            _secretManager = secretManager;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Get the authorization level for the current request
            (string name, AuthorizationLevel requestAuthorizationLevel) = await GetAuthorizationKeyInfoAsync(Context.Request, _secretManager);

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

                var identity = new ClaimsIdentity(claims, AuthLevelAuthenticationDefaults.AuthenticationScheme);
                return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
            }
            else
            {
                return AuthenticateResult.NoResult();
            }
        }

        internal static async Task<(string, AuthorizationLevel)> GetAuthorizationKeyInfoAsync(HttpRequest request, ISecretManager secretManager)
        {
            // first see if a key value is specified via headers or query string (header takes precedence)
            string keyValue = null;
            if (request.Headers.TryGetValue(FunctionsKeyHeaderName, out StringValues values))
            {
                keyValue = values.First();
            }
            else if (request.Query.TryGetValue("code", out values))
            {
                keyValue = values.First();
            }

            if (!string.IsNullOrEmpty(keyValue))
            {
                // see if the key specified is the master key
                HostSecretsInfo hostSecrets = await secretManager.GetHostSecretsAsync().ConfigureAwait(false);
                if (!string.IsNullOrEmpty(hostSecrets.MasterKey) &&
                    Key.SecretValueEquals(keyValue, hostSecrets.MasterKey))
                {
                    return (ScriptConstants.DefaultMasterKeyName, AuthorizationLevel.Admin);
                }

                if (HasMatchingKey(hostSecrets.SystemKeys, keyValue, out string keyName))
                {
                    return (keyName, AuthorizationLevel.System);
                }

                // see if the key specified matches the host function key
                if (HasMatchingKey(hostSecrets.FunctionKeys, keyValue, out keyName))
                {
                    return (keyName, AuthorizationLevel.Function);
                }

                // If there is a function specific key specified try to match against that
                IFunctionExecutionFeature executionFeature = request.HttpContext.Features.Get<IFunctionExecutionFeature>();
                if (executionFeature != null)
                {
                    IDictionary<string, string> functionSecrets = await secretManager.GetFunctionSecretsAsync(executionFeature.Descriptor.Name);
                    if (HasMatchingKey(functionSecrets, keyValue, out keyName))
                    {
                        return (keyName, AuthorizationLevel.Function);
                    }
                }
            }

            return (null, AuthorizationLevel.Anonymous);
        }

        private static bool HasMatchingKey(IDictionary<string, string> secrets, string keyValue, out string matchedKeyName)
        {
            matchedKeyName = null;
            if (secrets == null)
            {
                return false;
            }

            string matchedValue;
            (matchedKeyName, matchedValue) = secrets.FirstOrDefault(s => Key.SecretValueEquals(s.Value, keyValue));

            return matchedValue != null;
        }
    }
}
