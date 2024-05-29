// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.Filters;
using Microsoft.Azure.Web.DataProtection;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.IdentityModel.Tokens;
using static Microsoft.Azure.WebJobs.Script.EnvironmentSettingNames;
using static Microsoft.Azure.WebJobs.Script.ScriptConstants;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Filters
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
    public sealed class JwtAuthenticationAttribute : Attribute, IAuthenticationFilter
    {
        public bool AllowMultiple => false;

        public Task AuthenticateAsync(HttpAuthenticationContext context, CancellationToken cancellationToken)
        {
            // By default, tokens are passed via the standard Authorization Bearer header. However we also support
            // passing tokens via the x-ms-site-token header.
            string token = null;
            if (context.Request.Headers.TryGetValues(ScriptConstants.SiteTokenHeaderName, out IEnumerable<string> values))
            {
                token = values.FirstOrDefault();
            }
            else
            {
                // check the standard Authorization header
                AuthenticationHeaderValue authorization = context.Request.Headers.Authorization;
                if (authorization != null && string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
                {
                    token = authorization.Parameter;
                }
            }

            if (!string.IsNullOrEmpty(token))
            {
                var signingKeys = SecretsUtility.GetTokenIssuerSigningKeys();
                if (signingKeys.Length > 0)
                {
                    var validationParameters = CreateTokenValidationParameters(signingKeys);
                    if (JwtGenerator.IsTokenValid(token, validationParameters))
                    {
                        context.Request.SetAuthorizationLevel(AuthorizationLevel.Admin);
                    }
                }
            }

            return Task.CompletedTask;
        }

        public Task ChallengeAsync(HttpAuthenticationChallengeContext context, CancellationToken cancellationToken) => Task.CompletedTask;

        internal static TokenValidationParameters CreateTokenValidationParameters(SymmetricSecurityKey[] signingKeys)
        {
            string siteName = ScriptSettingsManager.Instance.GetSetting(AzureWebsiteName);
            string runtimeSiteName = ScriptSettingsManager.Instance.GetSetting(AzureWebsiteRuntimeSiteName);
            var audiences = new List<string>
            {
                string.Format(SiteAzureFunctionsUriFormat, siteName),
                string.Format(SiteUriFormat, siteName)
            };

            if (!string.IsNullOrEmpty(runtimeSiteName) && !string.Equals(siteName, runtimeSiteName, StringComparison.OrdinalIgnoreCase))
            {
                // on a non-production slot, the runtime site name will differ from the site name
                // we allow both for audience
                audiences.Add(string.Format(SiteUriFormat, runtimeSiteName));
            }

            var validationParameters = new TokenValidationParameters()
            {
                IssuerSigningKeys = signingKeys,
                AudienceValidator = AudienceValidator,
                IssuerValidator = IssuerValidator,
                ValidAudiences = audiences,
                ValidIssuers = new string[]
                {
                    AppServiceCoreUri,
                    string.Format(ScmSiteUriFormat, siteName),
                    string.Format(SiteUriFormat, siteName)
                }
            };

            return validationParameters;
        }

        private static string IssuerValidator(string issuer, SecurityToken securityToken, TokenValidationParameters validationParameters)
        {
            if (!validationParameters.ValidIssuers.Any(p => string.Equals(issuer, p, StringComparison.OrdinalIgnoreCase)))
            {
                throw new SecurityTokenInvalidIssuerException("IDX10205: Issuer validation failed.")
                {
                    InvalidIssuer = issuer,
                };
            }

            return issuer;
        }

        private static bool AudienceValidator(IEnumerable<string> audiences, SecurityToken securityToken, TokenValidationParameters validationParameters)
        {
            foreach (string audience in audiences)
            {
                if (validationParameters.ValidAudiences.Any(p => string.Equals(audience, p, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}