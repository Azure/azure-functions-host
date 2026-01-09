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
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication.Jwt;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
        {
            builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<JwtBearerOptions>, JwtBearerPostConfigureOptions>());
            return builder.AddScheme<JwtBearerOptions, ScriptJwtBearerHandler>(
                JwtBearerDefaults.AuthenticationScheme, displayName: null, ConfigureOptions);
        }

        private static void ConfigureOptions(JwtBearerOptions options)
        {
            options.Events = new JwtBearerEvents()
            {
                OnMessageReceived = c =>
                {
                    var loggerFactory = c.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>();
                    var logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryHostAuthentication);
                    logger.LogInformation("JWT OnMessageReceived: Checking for bearer token in request headers");

                    // By default, tokens are passed via the standard Authorization Bearer header. However we also support
                    // passing tokens via the x-ms-site-token header.
                    if (c.Request.Headers.TryGetValue(ScriptConstants.SiteTokenHeaderName, out StringValues values))
                    {
                        // the token we set here will be the one used - Authorization header won't be checked.
                        c.Token = values.FirstOrDefault();
                        logger.LogInformation("JWT OnMessageReceived: Found token in x-ms-site-token header");
                    }
                    else if (c.Request.Headers.ContainsKey("Authorization"))
                    {
                        logger.LogInformation("JWT OnMessageReceived: Found Authorization header");
                    }

                    // Temporary: Tactical fix to address specialization issues. This should likely be moved to a token validator
                    // TODO: DI (FACAVAL) This will be fixed once the permanent fix is in place
                    if (_specialized == 0 && !SystemEnvironment.Instance.IsPlaceholderModeEnabled() && Interlocked.CompareExchange(ref _specialized, 1, 0) == 0)
                    {
                        logger.LogInformation("JWT OnMessageReceived: Creating token validation parameters after specialization");
                        options.TokenValidationParameters = CreateTokenValidationParameters();
                    }

                    return Task.CompletedTask;
                },
                OnTokenValidated = c =>
                {
                    var loggerFactory = c.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>();
                    var logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryHostAuthentication);
                    logger.LogInformation("JWT OnTokenValidated: Token successfully validated. Issuer={Issuer}", c.SecurityToken.Issuer);

                    var claims = new List<Claim>
                    {
                        new Claim(SecurityConstants.AuthLevelClaimType, AuthorizationLevel.Admin.ToString())
                    };

                    if (!string.Equals(c.SecurityToken.Issuer, ScriptConstants.AppServiceCoreUri, StringComparison.OrdinalIgnoreCase))
                    {
                        claims.Add(new Claim(SecurityConstants.InvokeClaimType, "true"));
                    }

                    if (string.Equals(c.SecurityToken.Issuer, ScriptConstants.LegionCoreUri, StringComparison.OrdinalIgnoreCase))
                    {
                        claims.Add(new Claim(SecurityConstants.AssignUnencryptedClaimType, "true"));
                    }

                    c.Principal.AddIdentity(new ClaimsIdentity(claims));
                    c.Success();

                    logger.LogInformation("JWT OnTokenValidated: Added {ClaimCount} claims to principal", claims.Count);

                    return Task.CompletedTask;
                },
                OnAuthenticationFailed = c =>
                {
                    LogAuthenticationFailure(c);
                    return Task.CompletedTask;
                }
            };

            options.TokenValidationParameters = CreateTokenValidationParameters();

            // TODO: DI (FACAVAL) Remove this once the work above is completed.
            if (!SystemEnvironment.Instance.IsPlaceholderModeEnabled())
            {
                // We're not in standby mode, so flag as specialized
                _specialized = 1;
            }
        }

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
                else if (SystemEnvironment.Instance.IsLinuxConsumptionOnAtlas() ||
                    SystemEnvironment.Instance.IsLinuxConsumptionOnLegion())
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
                string.Format(SiteAzureFunctionsUriFormat, siteName),
                string.Format(SiteUriFormat, siteName)
            };

            if (!string.IsNullOrEmpty(runtimeSiteName) && !string.Equals(siteName, runtimeSiteName, StringComparison.OrdinalIgnoreCase))
            {
                // on a non-production slot, the runtime site name will differ from the site name
                // we allow both for audience
                audiences.Add(string.Format(SiteUriFormat, runtimeSiteName));
            }

            return audiences;
        }

        public static TokenValidationParameters CreateTokenValidationParameters()
        {
            System.Diagnostics.Trace.WriteLine($"[{ScriptConstants.LogCategoryHostAuthentication}] CreateTokenValidationParameters: Starting token validation parameter creation");
            var signingKeys = SecretsUtility.GetTokenIssuerSigningKeys();
            System.Diagnostics.Trace.WriteLine($"[{ScriptConstants.LogCategoryHostAuthentication}] CreateTokenValidationParameters: Retrieved {signingKeys.Length} signing keys");

            // There are two separate CodeQL alerts for the same issue. The double comment on same line is intentional.
            // CodeQL [SM04555] this handler does not verify AAD tokens. It verifies tokens issued by the platform. // CodeQL [SM04554] this handler does not verify AAD tokens. It verifies tokens issued by the platform.
            var result = new TokenValidationParameters();
            if (signingKeys.Length > 0)
            {
                result.IssuerSigningKeys = signingKeys;
                result.AudienceValidator = AudienceValidator;
                result.IssuerValidator = IssuerValidator;
                result.ValidAudiences = GetValidAudiences();
                result.ValidIssuers =
                [
                    AppServiceCoreUri,
                    LegionCoreUri,
                    string.Format(ScmSiteUriFormat, ScriptSettingsManager.Instance.GetSetting(AzureWebsiteName)),
                    string.Format(SiteUriFormat, ScriptSettingsManager.Instance.GetSetting(AzureWebsiteName))
                ];
            }

            return result;
        }

        private static string IssuerValidator(string issuer, SecurityToken securityToken, TokenValidationParameters validationParameters)
        {
            System.Diagnostics.Trace.WriteLine($"[{ScriptConstants.LogCategoryHostAuthentication}] IssuerValidator: Validating issuer '{issuer}'");
            if (!validationParameters.ValidIssuers.Any(p => string.Equals(issuer, p, StringComparison.OrdinalIgnoreCase)))
            {
                System.Diagnostics.Trace.WriteLine($"[{ScriptConstants.LogCategoryHostAuthentication}] IssuerValidator: Issuer validation FAILED for '{issuer}'");
                throw new SecurityTokenInvalidIssuerException("IDX10205: Issuer validation failed.")
                {
                    InvalidIssuer = issuer,
                };
            }

            System.Diagnostics.Trace.WriteLine($"[{ScriptConstants.LogCategoryHostAuthentication}] IssuerValidator: Issuer validation SUCCEEDED");
            return issuer;
        }

        private static bool AudienceValidator(IEnumerable<string> audiences, SecurityToken securityToken, TokenValidationParameters validationParameters)
        {
            System.Diagnostics.Trace.WriteLine($"[{ScriptConstants.LogCategoryHostAuthentication}] AudienceValidator: Validating {audiences.Count()} audience(s)");
            foreach (string audience in audiences)
            {
                if (validationParameters.ValidAudiences.Any(p => string.Equals(audience, p, StringComparison.OrdinalIgnoreCase)))
                {
                    System.Diagnostics.Trace.WriteLine($"[{ScriptConstants.LogCategoryHostAuthentication}] AudienceValidator: Audience validation SUCCEEDED for '{audience}'");
                    return true;
                }
            }

            System.Diagnostics.Trace.WriteLine($"[{ScriptConstants.LogCategoryHostAuthentication}] AudienceValidator: Audience validation FAILED");
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
