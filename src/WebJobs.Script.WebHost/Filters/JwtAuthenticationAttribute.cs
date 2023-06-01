﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
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
                    var validationParameters = new TokenValidationParameters()
                    {
                        IssuerSigningKeys = signingKeys,
                        ValidateAudience = true,
                        ValidateIssuer = true,
                        ValidAudiences = new string[]
                        {
                            string.Format(SiteAzureFunctionsUriFormat, ScriptSettingsManager.Instance.GetSetting(AzureWebsiteName)),
                            string.Format(SiteUriFormat, ScriptSettingsManager.Instance.GetSetting(AzureWebsiteName))
                        },
                        ValidIssuers = new string[]
                        {
                            AppServiceCoreUri,
                            string.Format(ScmSiteUriFormat, ScriptSettingsManager.Instance.GetSetting(AzureWebsiteName)),
                            string.Format(SiteUriFormat, ScriptSettingsManager.Instance.GetSetting(AzureWebsiteName))
                        }
                    };

                    if (JwtGenerator.IsTokenValid(token, validationParameters))
                    {
                        context.Request.SetAuthorizationLevel(AuthorizationLevel.Admin);
                    }
                }
            }

            return Task.CompletedTask;
        }

        public Task ChallengeAsync(HttpAuthenticationChallengeContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}