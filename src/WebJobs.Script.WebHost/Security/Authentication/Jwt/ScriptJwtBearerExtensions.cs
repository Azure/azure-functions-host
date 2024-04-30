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
        private static double _specialized = 0;

        public static AuthenticationBuilder AddScriptJwtBearer(this AuthenticationBuilder builder)
            => builder.AddJwtBearer(o =>
                        {
                            o.Events = new JwtBearerEvents()
                            {
                                OnMessageReceived = c =>
                                {
                                    Console.WriteLine("[TEST] OnMessageReceived 1");
                                    // By default, tokens are passed via the standard Authorization Bearer header. However we also support
                                    // passing tokens via the x-ms-site-token header.
                                    if (c.Request.Headers.TryGetValue(ScriptConstants.SiteTokenHeaderName, out StringValues values))
                                    {
                                        // the token we set here will be the one used - Authorization header won't be checked.
                                        c.Token = values.FirstOrDefault();
                                        Console.WriteLine("[TEST] Token received: {0}", c.Token);
                                    }

                                    Console.WriteLine("[TEST] OnMessageReceived 2");

                                    // Temporary: Tactical fix to address specialization issues. This should likely be moved to a token validator
                                    // TODO: DI (FACAVAL) This will be fixed once the permanent fix is in place
                                    if (_specialized == 0 && !SystemEnvironment.Instance.IsPlaceholderModeEnabled() && Interlocked.CompareExchange(ref _specialized, 1, 0) == 0)
                                    {
                                        Console.WriteLine("[TEST] OnMessageReceived not Specialized yet");
                                        o.TokenValidationParameters = CreateTokenValidationParameters();
                                    }

                                    Console.WriteLine("[TEST] OnMessageReceived 3");

                                    return Task.CompletedTask;
                                },
                                OnTokenValidated = c =>
                                {
                                    Console.WriteLine("[TEST] OnTokenValidated 1");
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
                                    Console.WriteLine("[TEST] OnAuthenticationFailed");
                                    LogAuthenticationFailure(c);

                                    return Task.CompletedTask;
                                }
                            };

                            Console.WriteLine("[TEST] AddScriptJwt 1");

                            o.TokenValidationParameters = CreateTokenValidationParameters();

                            Console.WriteLine("[TEST] AddScriptJwt 2");

                            // TODO: DI (FACAVAL) Remove this once the work above is completed.
                            if (!SystemEnvironment.Instance.IsPlaceholderModeEnabled())
                            {
                                Console.WriteLine("[TEST] AddScriptJwt 3");
                                // We're not in standby mode, so flag as specialized
                                _specialized = 1;
                            }
                        });

        private static string[] GetValidAudiences()
        {
            if (SystemEnvironment.Instance.IsPlaceholderModeEnabled()
                && SystemEnvironment.Instance.IsLinuxConsumptionOnAtlas())
            {
                Console.WriteLine("[TEST] GetValidAudiences 1, returning true for cv1 migration");
                return new string[]
                {
                    ScriptSettingsManager.Instance.GetSetting(ContainerName)
                };
            }

            Console.WriteLine("[TEST] GetValidAudiences 2");

            Console.WriteLine($"[TEST] AzureWebsiteName: {string.Format(SiteAzureFunctionsUriFormat, ScriptSettingsManager.Instance.GetSetting(AzureWebsiteName))}");
            Console.WriteLine($"[TEST] AzureWebsiteName 2: {string.Format(SiteUriFormat, ScriptSettingsManager.Instance.GetSetting(AzureWebsiteName))}");

            return new string[]
            {
                string.Format(SiteAzureFunctionsUriFormat, ScriptSettingsManager.Instance.GetSetting(AzureWebsiteName)),
                string.Format(SiteUriFormat, ScriptSettingsManager.Instance.GetSetting(AzureWebsiteName))
            };
        }

        public static TokenValidationParameters CreateTokenValidationParameters()
        {
            Console.WriteLine("[TEST] CreateTokenValidationParameters 1");
            var signingKeys = SecretsUtility.GetTokenIssuerSigningKeys();
            Console.WriteLine("[TEST] CreateTokenValidationParameters 2");
            var result = new TokenValidationParameters();
            Console.WriteLine("[TEST] CreateTokenValidationParameters 3");
            if (signingKeys.Length > 0)
            {
                Console.WriteLine("[TEST] CreateTokenValidationParameters 4");
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
                // console log valid issuers
                Console.WriteLine($"[TEST] Valid Issuers: {string.Join(", ", result.ValidIssuers)}");
            }

            Console.WriteLine("[TEST] CreateTokenValidationParameters 5");            

            return result;
        }

        private static string IssuerValidator(string issuer, SecurityToken securityToken, TokenValidationParameters validationParameters)
        {
            if (!validationParameters.ValidIssuers.Any(p => string.Equals(issuer, p, StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine($"[TEST] IssuerValidator failed: {issuer}");
                throw new SecurityTokenInvalidIssuerException("IDX10205: Issuer validation failed.")
                {
                    InvalidIssuer = issuer,
                };
            }

            Console.WriteLine($"[TEST] IssuerValidator: {issuer} is valid");

            return issuer;
        }

        private static bool AudienceValidator(IEnumerable<string> audiences, SecurityToken securityToken, TokenValidationParameters validationParameters)
        {
            foreach (string audience in audiences)
            {
                Console.WriteLine($"[TEST] AudienceValidator: {audience}");
                if (validationParameters.ValidAudiences.Any(p => string.Equals(audience, p, StringComparison.OrdinalIgnoreCase)))
                {
                    Console.WriteLine($"[TEST] AudienceValidator: {audience} is valid");
                    return true;
                }
            }

            return false;
        }

        private static void LogAuthenticationFailure(AuthenticationFailedContext context)
        {
            Console.WriteLine("[TEST] Console Output: LogAuthenticationFailure()");
            if (!context.Request.IsAdminRequest())
            {
                return;
            }

            var loggerFactory = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryHostAuthentication);

            Console.WriteLine("[TEST] Console Output: Authentication failed.");

            string message = null;
            switch (context.Exception)
            {
                case SecurityTokenInvalidIssuerException iex:
                    message = $"Token issuer validation failed for issuer '{iex.InvalidIssuer}'.";
                    Console.WriteLine($"[TEST] SecurityTokenInvalidIssuerException: {message}");
                    break;
                case SecurityTokenInvalidAudienceException iaex:
                    message = $"Token audience validation failed for audience '{iaex.InvalidAudience}'.";
                    Console.WriteLine($"[TEST] SecurityTokenInvalidAudienceException: {message}");
                    break;
                default:
                    message = $"Token validation failed.";
                    Console.WriteLine($"[TEST] Default: {message}");
                    break;
            }

            logger.LogDebug(context.Exception, message);
        }
    }
}
