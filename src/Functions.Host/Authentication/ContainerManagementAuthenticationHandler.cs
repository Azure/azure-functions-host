// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Authentication;

/// <summary>
/// Authenticates platform management calls with the receiving container's own key and pod identity.
/// This source is also compiled into WorkerProxy, independently of worker assignment and app authentication.
/// </summary>
internal sealed partial class ContainerManagementAuthenticationHandler(
    IOptionsMonitor<ContainerManagementAuthenticationHandler.Credentials> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    TimeProvider timeProvider) : AuthenticationHandler<ContainerManagementAuthenticationHandler.Credentials>(options, loggerFactory, encoder)
{
    internal const string SchemeName = "ContainerManagement";

    internal static void AddServices(IServiceCollection services)
    {
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddOptions<Credentials>(SchemeName)
            .Configure<IConfiguration, ILogger<Credentials>>(static (credentials, configuration, logger) =>
                credentials.Configure(configuration, logger))
            .ValidateOnStart();
        services.AddAuthentication()
            .AddScheme<Credentials, ContainerManagementAuthenticationHandler>(SchemeName, static _ => { });
        services.AddAuthorization(options => options.AddPolicy(SchemeName,
            policy => policy.AddAuthenticationSchemes(SchemeName).RequireAuthenticatedUser()));
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey(RuntimeWorkerAuthentication.SiteTokenHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        AuthenticationFailureReason reason = Options.Failure;
        if (reason is AuthenticationFailureReason.None
            && (Request.Headers[RuntimeWorkerAuthentication.SiteTokenHeader] is not [string]
                || Request.Headers[RuntimeWorkerAuthentication.SiteTokenHeader].ToString().Contains(',', StringComparison.Ordinal)))
        {
            reason = AuthenticationFailureReason.DuplicateToken;
        }
        if (reason is AuthenticationFailureReason.None
            && RuntimeWorkerAuthentication.ValidateManagementToken(Request.Headers[RuntimeWorkerAuthentication.SiteTokenHeader][0],
                Options.Key, Options.PodName, timeProvider.GetUtcNow(), out reason))
        {
            ClaimsIdentity identity = new([new Claim(ClaimTypes.NameIdentifier, Options.PodName)], Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(identity), Scheme.Name)));
        }

        Log.AuthenticationRejected(Logger, reason);

        return Task.FromResult(AuthenticateResult.Fail("Container management authentication failed."));
    }

    /// <inheritdoc />
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (!Request.Headers.ContainsKey(RuntimeWorkerAuthentication.SiteTokenHeader))
        {
            Log.AuthenticationRejected(Logger, AuthenticationFailureReason.MissingToken);
        }

        return base.HandleChallengeAsync(properties);
    }

    internal sealed class Credentials : AuthenticationSchemeOptions
    {
        public string PodName { get; private set; } = string.Empty;

        public byte[] Key { get; private set; } = [];

        public AuthenticationFailureReason Failure { get; private set; }

        internal void Configure(IConfiguration configuration, ILogger<Credentials> logger)
        {
            string? podName = configuration["WEBSITE_POD_NAME"];
            PodName = string.IsNullOrWhiteSpace(podName) ? configuration["CONTAINER_NAME"] ?? string.Empty : podName;
            string? encodedKey = configuration[RuntimeWorkerAuthentication.WorkerKeySetting];
            if (!RuntimeWorkerAuthentication.TryDecodeWorkerKey(encodedKey, out byte[] key))
            {
                Failure = string.IsNullOrWhiteSpace(encodedKey) ? AuthenticationFailureReason.MissingKey : AuthenticationFailureReason.InvalidKey;
                Log.ConfigurationUnavailable(logger, RuntimeWorkerAuthentication.WorkerKeySetting, Failure);
            }
            Key = key;
            if (string.IsNullOrWhiteSpace(PodName))
            {
                Failure = AuthenticationFailureReason.MissingIdentity;
                Log.ConfigurationUnavailable(logger, "WEBSITE_POD_NAME/CONTAINER_NAME", Failure);
            }
        }
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Warning, "Container management authentication failed: {Reason}.")]
        public static partial void AuthenticationRejected(ILogger logger, AuthenticationFailureReason reason);

        [LoggerMessage(1, LogLevel.Error, "Container management authentication configuration is unavailable: {Setting}, {Reason}.")]
        public static partial void ConfigurationUnavailable(ILogger logger, string setting, AuthenticationFailureReason reason);
    }
}
