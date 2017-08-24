// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Azure.Web.DataProtection;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication;
using Microsoft.IdentityModel.Tokens;
using static Microsoft.Azure.WebJobs.Script.EnvironmentSettingNames;
using static Microsoft.Azure.WebJobs.Script.ScriptConstants;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ScriptJwtBearerExtensions
    {
        public static AuthenticationBuilder AddScriptJwtBearer(this AuthenticationBuilder builder)
            => builder.AddJwtBearer(o =>
                        {
                            o.Events = new JwtBearerEvents()
                            {
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
                            string defaultKey = Util.GetDefaultKeyValue();
                            if (defaultKey != null)
                            {
                                // TODO: Once ScriptSettingsManager is gone, Audience and Issuer shouold be pulled from configuration.
                                o.TokenValidationParameters = new TokenValidationParameters()
                                {
                                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(defaultKey)),
                                    ValidateAudience = true,
                                    ValidateIssuer = true,
                                    ValidAudience = string.Format(AdminJwtValidAudienceFormat, ScriptSettingsManager.Instance.GetSetting(AzureWebsiteName)),
                                    ValidIssuer = string.Format(AdminJwtValidIssuerFormat, ScriptSettingsManager.Instance.GetSetting(AzureWebsiteName))
                                };
                            }
                        });
    }
}
