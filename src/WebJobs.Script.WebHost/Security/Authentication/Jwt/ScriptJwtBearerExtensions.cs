﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using static Microsoft.Azure.WebJobs.Script.EnvironmentSettingNames;
using static Microsoft.Azure.WebJobs.Script.ScriptConstants;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ScriptJwtBearerExtensions
    {
        private static double _specialized = 0;

        public static AuthenticationBuilder AddScriptJwtBearer(this AuthenticationBuilder builder)
            => builder.AddJwtBearer(o =>
                        {
                            o.Events = new JwtBearerEvents()
                            {
                                OnMessageReceived = c =>
                                {
                                    // By default, tokens are passed via the standard Authorization Bearer header. However we also support
                                    // passing tokens via the x-ms-site-token header.
                                    if (c.Request.Headers.TryGetValue(ScriptConstants.SiteTokenHeaderName, out StringValues values))
                                    {
                                        // the token we set here will be the one used - Authorization header won't be checked.
                                        c.Token = values.FirstOrDefault();
                                    }

                                    // Temporary: Tactical fix to address specialization issues. This should likely be moved to a token validator
                                    // TODO: DI (FACAVAL) This will be fixed once the permanent fix is in place
                                    if (_specialized == 0 && !SystemEnvironment.Instance.IsPlaceholderModeEnabled() && Interlocked.CompareExchange(ref _specialized, 1, 0) == 0)
                                    {
                                        o.TokenValidationParameters = CreateTokenValidationParameters();
                                    }

                                    return Task.CompletedTask;
                                },
                                OnTokenValidated = c =>
                                {
                                    c.Principal.AddIdentity(new ClaimsIdentity(new Claim[]
                                    {
                                        new Claim(SecurityConstants.AuthLevelClaimType, AuthorizationLevel.Admin.ToString())
                                    }));

                                    c.Success();

                                    return Task.CompletedTask;
                                }
                            };

                            o.TokenValidationParameters = CreateTokenValidationParameters();

                            // TODO: DI (FACAVAL) Remove this once the work above is completed.
                            if (!SystemEnvironment.Instance.IsPlaceholderModeEnabled())
                            {
                                // We're not in standby mode, so flag as specialized
                                _specialized = 1;
                            }
                        });

        private static TokenValidationParameters CreateTokenValidationParameters()
        {
            var result = new TokenValidationParameters();
            if (SecretsUtility.TryGetEncryptionKey(out string key))
            {
                // TODO: Once ScriptSettingsManager is gone, Audience and Issuer should be pulled from configuration.
                result.IssuerSigningKeys = new SecurityKey[]
                {
                    new SymmetricSecurityKey(key.ToKeyBytes()),
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
                };
                result.ValidateAudience = true;
                result.ValidateIssuer = true;
                result.ValidAudiences = new string[]
                {
                    string.Format(AdminJwtSiteFunctionsValidAudienceFormat, ScriptSettingsManager.Instance.GetSetting(AzureWebsiteName)),
                    string.Format(AdminJwtSiteValidAudienceFormat, ScriptSettingsManager.Instance.GetSetting(AzureWebsiteName))
                };
                result.ValidIssuers = new string[]
                {
                    AdminJwtAppServiceIssuer,
                    string.Format(AdminJwtScmValidIssuerFormat, ScriptSettingsManager.Instance.GetSetting(AzureWebsiteName)),
                    string.Format(AdminJwtSiteValidIssuerFormat, ScriptSettingsManager.Instance.GetSetting(AzureWebsiteName))
                };
            }

            return result;
        }
    }
}
