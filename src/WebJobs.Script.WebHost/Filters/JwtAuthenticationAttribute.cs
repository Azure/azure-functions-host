// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.Filters;
using Microsoft.Azure.Web.DataProtection;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.IdentityModel.Tokens;
using static Microsoft.Azure.WebJobs.Script.Config.ScriptSettingsManager;
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
            AuthenticationHeaderValue authorization = context.Request.Headers.Authorization;

            if (authorization != null && string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
            {
                string defaultKey = Util.GetDefaultKeyValue();
                if (defaultKey != null)
                {
                    var validationParameters = new TokenValidationParameters()
                    {
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(defaultKey)),
                        ValidateAudience = true,
                        ValidateIssuer = true,
                        ValidAudience = string.Format(AdminJwtValidAudienceFormat, Instance.GetSetting(AzureWebsiteName)),
                        ValidIssuer = string.Format(AdminJwtValidIssuerFormat, Instance.GetSetting(AzureWebsiteName))
                    };

                    if (JwtGenerator.IsTokenValid(authorization.Parameter, validationParameters))
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