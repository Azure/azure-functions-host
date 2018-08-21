// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Azure.Web.DataProtection;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication;
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
                                    // Temporary: Tactical fix to address specialization issues. This should likely be moved to a token validator
                                    // TODO: DI (FACAVAL) This will be fixed once the permanent fix is in plance
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

                            // TODO: DI (FACAVAL) Remove this once th work above is completed.
                            if (!SystemEnvironment.Instance.IsPlaceholderModeEnabled())
                            {
                                // We're not in standby mode, so flag as specialized
                                _specialized = 1;
                            }
                        });

        private static TokenValidationParameters CreateTokenValidationParameters()
        {
            string defaultKey = Util.GetDefaultKeyValue();

            var result = new TokenValidationParameters();
            if (defaultKey != null)
            {
                // TODO: Once ScriptSettingsManager is gone, Audience and Issuer shouold be pulled from configuration.
                result.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(defaultKey));
                result.ValidateAudience = true;
                result.ValidateIssuer = true;
                result.ValidAudience = string.Format(AdminJwtValidAudienceFormat, ScriptSettingsManager.Instance.GetSetting(AzureWebsiteName));
                result.ValidIssuer = string.Format(AdminJwtValidIssuerFormat, ScriptSettingsManager.Instance.GetSetting(AzureWebsiteName));
            }

            return result;
        }
    }
}
