// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication.Shared;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;

namespace Microsoft.Azure.Functions.WorkerProxy.Authentication;

/// <summary>
/// JWT bearer authentication that mirrors the Functions runtime's
/// <c>x-ms-site-token</c> scheme so calls into the worker proxy's admin
/// endpoints can be authenticated with the same tokens NNA already mints
/// for the runtime's <c>/admin/host/assign</c> call.
///
/// Tokens are signed with the container's encryption key
/// (<c>CONTAINER_ENCRYPTION_KEY</c>, falling back to
/// <c>WEBSITE_AUTH_ENCRYPTION_KEY</c>); the audience is the pod or container
/// identity (the proxy never takes on the customer's site identity); and the
/// issuer is one of the platform issuers (Antares App Service core or Legion
/// core). The token may be presented either via the standard
/// <c>Authorization: Bearer ...</c> header or via the <c>x-ms-site-token</c>
/// header, identical to the runtime.
///
/// Behavior matches the runtime when no encryption key is configured: the
/// signing-key list is empty, validation parameters have no
/// <c>IssuerSigningKeys</c>, and every token is rejected (fail closed).
/// </summary>
internal static class ContainerJwtAuth
{
    public const string SiteTokenHeaderName = "x-ms-site-token";

    // Mirrors src/WebJobs.Script/ScriptConstants.cs.
    public const string AppServiceCoreUri = "https://appservice.core.azurewebsites.net";
    public const string LegionCoreUri = "https://legion.core.azurewebsites.net";

    // Mirrors src/WebJobs.Script/Environment/EnvironmentSettingNames.cs.
    public const string ContainerEncryptionKey = "CONTAINER_ENCRYPTION_KEY";
    public const string WebSiteAuthEncryptionKey = "WEBSITE_AUTH_ENCRYPTION_KEY";
    public const string WebsitePodName = "WEBSITE_POD_NAME";
    public const string ContainerName = "CONTAINER_NAME";

    /// <summary>
    /// Registers JWT bearer authentication and authorization services so that
    /// endpoints can be protected with <c>RequireAuthorization()</c>.
    /// Reads environment variables via <see cref="Environment.GetEnvironmentVariable(string)"/>.
    /// </summary>
    public static IServiceCollection AddContainerJwtAuth(this IServiceCollection services)
        => services.AddContainerJwtAuth(Environment.GetEnvironmentVariable);

    /// <summary>
    /// Test-friendly overload that takes an explicit environment-variable
    /// lookup so tests don't need to mutate process environment.
    /// </summary>
    public static IServiceCollection AddContainerJwtAuth(this IServiceCollection services, Func<string, string?> getEnv)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(getEnv);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options => ConfigureJwtBearer(options, getEnv));

        services.AddAuthorization();

        return services;
    }

    internal static void ConfigureJwtBearer(JwtBearerOptions options, Func<string, string?> getEnv)
    {
        options.TokenValidationParameters = CreateTokenValidationParameters(getEnv);

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = c =>
            {
                // The proxy mirrors the runtime: tokens may arrive on the standard
                // Authorization header OR on x-ms-site-token. When the latter is
                // present, it takes precedence (the runtime makes the same choice).
                if (c.Request.Headers.TryGetValue(SiteTokenHeaderName, out StringValues values))
                {
                    c.Token = values.FirstOrDefault();
                }

                return Task.CompletedTask;
            },
            OnTokenValidated = c =>
            {
                // Add a single Admin claim so RequireAuthorization() succeeds.
                // The proxy doesn't have multi-tier auth like the runtime; any
                // valid platform-issued token is sufficient to call admin endpoints.
                var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Role, "Admin")
                ]);
                c.Principal?.AddIdentity(identity);
                c.Success();

                return Task.CompletedTask;
            },
            OnAuthenticationFailed = c =>
            {
                var loggerFactory = c.HttpContext.RequestServices.GetService<ILoggerFactory>();
                var logger = loggerFactory?.CreateLogger("Microsoft.Azure.Functions.WorkerProxy.Authentication");
                if (logger is null)
                {
                    return Task.CompletedTask;
                }

                string message = c.Exception switch
                {
                    SecurityTokenInvalidIssuerException iex => $"Token issuer validation failed for issuer '{iex.InvalidIssuer}'.",
                    SecurityTokenInvalidAudienceException iaex => $"Token audience validation failed for audience '{iaex.InvalidAudience}'.",
                    SecurityTokenExpiredException => "Token validation failed: token expired.",
                    SecurityTokenSignatureKeyNotFoundException => "Token validation failed: signing key not found.",
                    _ => "Token validation failed."
                };

                logger.LogDebug(c.Exception, "{Message}", message);
                return Task.CompletedTask;
            }
        };
    }

    internal static TokenValidationParameters CreateTokenValidationParameters(Func<string, string?> getEnv)
    {
        var keys = GetSigningKeys(getEnv);
        var audiences = GetValidAudiences(getEnv);

        // Match the runtime exactly when no key is configured: leave
        // IssuerSigningKeys unset so every token fails validation.
        if (keys.Length == 0)
        {
            // CodeQL [SM04555] this handler does not verify AAD tokens. It verifies tokens issued by the platform. 
            // CodeQL [SM04554] this handler does not verify AAD tokens. It verifies tokens issued by the platform.
            return new TokenValidationParameters();
        }

        // CodeQL [SM04555] this handler does not verify AAD tokens. It verifies tokens issued by the platform. 
        // CodeQL [SM04554] this handler does not verify AAD tokens. It verifies tokens issued by the platform.
        return new TokenValidationParameters
        {
            IssuerSigningKeys = keys,
            ValidIssuers = [AppServiceCoreUri, LegionCoreUri],
            ValidAudiences = audiences,
            IssuerValidator = SiteTokenValidators.IssuerValidator,
            AudienceValidator = SiteTokenValidators.AudienceValidator,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
        };
    }

    internal static SymmetricSecurityKey[] GetSigningKeys(Func<string, string?> getEnv)
    {
        // Order matches runtime's SecretsUtility: CONTAINER_ENCRYPTION_KEY first,
        // then WEBSITE_AUTH_ENCRYPTION_KEY. The runtime additionally falls back
        // to Microsoft.Azure.Web.DataProtection's default key, but that path is
        // host-only (not AOT-safe and not present in container scenarios where
        // the proxy is the entry point).
        var keys = new List<SymmetricSecurityKey>(capacity: 2);

        TryAddKey(getEnv(ContainerEncryptionKey), keys);
        TryAddKey(getEnv(WebSiteAuthEncryptionKey), keys);

        return keys.ToArray();
    }

    internal static string[] GetValidAudiences(Func<string, string?> getEnv)
    {
        // The proxy is always pod/container-identity (it never specializes to
        // a customer site), so the only valid audiences are the pod name
        // (Flex Consumption) or the container name (Atlas / Legion v1).
        var audiences = new List<string>(capacity: 2);

        AddIfPresent(getEnv(WebsitePodName), audiences);
        AddIfPresent(getEnv(ContainerName), audiences);

        return audiences.ToArray();
    }

    private static void TryAddKey(string? rawKey, List<SymmetricSecurityKey> keys)
    {
        if (string.IsNullOrEmpty(rawKey))
        {
            return;
        }

        // Mirrors the runtime: if a configured encryption key is malformed,
        // SiteTokenKeyParser.ToKeyBytes throws FormatException and startup
        // fails loudly. The runtime's SecretsUtility.GetTokenIssuerSigningKeys
        // does the same — no try/catch around .ToKeyBytes(). Silent
        // fall-through would leave operators chasing 401s with no signal.
        keys.Add(new SymmetricSecurityKey(SiteTokenKeyParser.ToKeyBytes(rawKey)));
    }

    private static void AddIfPresent(string? value, List<string> target)
    {
        if (!string.IsNullOrEmpty(value))
        {
            target.Add(value);
        }
    }
}
