// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using static Microsoft.Azure.WebJobs.Script.EnvironmentSettingNames;
using static Microsoft.Azure.WebJobs.Script.ScriptConstants;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ScriptJwtBearerExtensions
    {
        private const string DeploymentSlotSeparator = "__";
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
                        var claims = new List<Claim>
                        {
                            new Claim(SecurityConstants.AuthLevelClaimType, AuthorizationLevel.Admin.ToString())
                        };
                        if (!string.Equals(c.SecurityToken.Issuer, ScriptConstants.AppServiceCoreUri, StringComparison.OrdinalIgnoreCase))
                        {
                            claims.Add(new Claim(SecurityConstants.InvokeClaimType, "true"));
                        }

                        c.Principal.AddIdentity(new ClaimsIdentity(claims));

                        c.Success();

                        return Task.CompletedTask;
                    },
                    OnAuthenticationFailed = c =>
                    {
                        LogAuthenticationFailure(c);

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

        private static IEnumerable<string> GetValidAudiences()
        {
            if (SystemEnvironment.Instance.IsPlaceholderModeEnabled())
            {
                if (SystemEnvironment.Instance.IsFlexConsumptionSku())
                {
                    return new string[]
                    {
                        ScriptSettingsManager.Instance.GetSetting(WebsitePodName)
                    };
                }
                else if (SystemEnvironment.Instance.IsLinuxConsumptionOnAtlas())
                {
                    return new string[]
                    {
                        ScriptSettingsManager.Instance.GetSetting(ContainerName)
                    };
                }
            }

            string siteName = ScriptSettingsManager.Instance.GetSetting(AzureWebsiteName);
            string runtimeSiteName = ScriptSettingsManager.Instance.GetSetting(AzureWebsiteRuntimeSiteName);
            var audiences = new List<string>
            {
                string.Format(SiteUriFormat, siteName),
                string.Format(SiteAzureFunctionsUriFormat, siteName)
            };

            if (TryGetNormalizedSiteName(siteName, out string normalizedSiteName))
            {
                // If we're dealing with a runtime site name (e.g. https://test__5bb5.azurewebsites.net)
                // we want to add a normalized version (e.g. https://test.azurewebsites.net).
                audiences.Add(string.Format(SiteUriFormat, normalizedSiteName));
            }

            if (!string.IsNullOrEmpty(runtimeSiteName))
            {
                // In slots scenarios the runtime site name can differ from the site name
                // we allow both for audience.
                string audience = string.Format(SiteUriFormat, runtimeSiteName);
                if (!audiences.Contains(audience, StringComparer.OrdinalIgnoreCase))
                {
                    audiences.Add(audience);
                }
            }

            return audiences;
        }

        public static TokenValidationParameters CreateTokenValidationParameters()
        {
            var signingKeys = SecretsUtility.GetTokenIssuerSigningKeys();
            var result = new TokenValidationParameters();
            if (signingKeys.Length > 0)
            {
                result.IssuerSigningKeys = signingKeys;
                result.AudienceValidator = AudienceValidator;
                result.IssuerValidator = IssuerValidator;
                result.ValidAudiences = GetValidAudiences();
                result.ValidIssuers = new string[]
                {
                    AppServiceCoreUri,
                    string.Format(ScmSiteUriFormat, ScriptSettingsManager.Instance.GetSetting(AzureWebsiteName)),
                    string.Format(SiteUriFormat, ScriptSettingsManager.Instance.GetSetting(AzureWebsiteName))
                };
            }

            return result;
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

        internal static bool AudienceValidator(IEnumerable<string> audiences, SecurityToken securityToken, TokenValidationParameters validationParameters)
        {
            foreach (string audience in audiences)
            {
                // First see if we have an exact match.
                if (validationParameters.ValidAudiences.Any(p => string.Equals(audience, p, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                // In slots scenarios, the hostname of the incoming audience may include a runtime
                // site name slot component (e.g. https://test__5bb5.azurewebsites.net). We normalize
                // this by removing the slot component (e.g. "__5bb5") and check again.
                if (TryGetNormalizedAudience(audience, out string normalizedAudience) &&
                    validationParameters.ValidAudiences.Any(p => string.Equals(normalizedAudience, p, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetNormalizedAudience(string audience, out string normalizedAudience)
        {
            normalizedAudience = null;

            if (!string.IsNullOrEmpty(audience))
            {
                int left = audience.IndexOf(DeploymentSlotSeparator);
                if (left != -1)
                {
                    int right = audience.IndexOf('.', left);
                    int length = right - left;
                    if (length > 0)
                    {
                        normalizedAudience = audience.Remove(left, length);
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryGetNormalizedSiteName(string siteName, out string normalizedSiteName)
        {
            normalizedSiteName = null;

            if (!string.IsNullOrEmpty(siteName))
            {
                int idx = siteName.IndexOf(DeploymentSlotSeparator);
                if (idx != -1)
                {
                    normalizedSiteName = siteName.Substring(0, idx);
                    return true;
                }
            }

            return false;
        }

        private static void LogAuthenticationFailure(AuthenticationFailedContext context)
        {
            if (!context.Request.IsAdminRequest())
            {
                return;
            }

            var loggerFactory = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryHostAuthentication);

            string message = null;
            switch (context.Exception)
            {
                case SecurityTokenInvalidIssuerException iex:
                    message = $"Token issuer validation failed for issuer '{iex.InvalidIssuer}'.";
                    break;
                case SecurityTokenInvalidAudienceException iaex:
                    message = $"Token audience validation failed for audience '{iaex.InvalidAudience}'.";
                    break;
                default:
                    message = $"Token validation failed.";
                    break;
            }

            logger.LogDebug(context.Exception, message);
        }
    }
}
