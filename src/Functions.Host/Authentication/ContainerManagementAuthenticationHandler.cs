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
            .Configure<IConfiguration>(static (credentials, configuration) => credentials.Configure(configuration))
            .Validate(static credentials => credentials.Key.Length >= 32,
                $"{RuntimeWorkerAuthentication.WorkerKeySetting} must decode to at least 32 bytes.")
            .Validate(static credentials => !string.IsNullOrWhiteSpace(credentials.PodName),
                "WEBSITE_POD_NAME or CONTAINER_NAME is required.")
            .ValidateOnStart();
        services.AddAuthentication()
            .AddScheme<Credentials, ContainerManagementAuthenticationHandler>(SchemeName, static _ => { });
        services.AddAuthorization(options => options.AddPolicy(SchemeName,
            policy => policy.AddAuthenticationSchemes(SchemeName).RequireAuthenticatedUser()));
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(RuntimeWorkerAuthentication.SiteTokenHeader, out var token))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        AuthenticationFailureReason reason;
        if (token is not [string] || token.ToString().Contains(',', StringComparison.Ordinal))
        {
            reason = AuthenticationFailureReason.MalformedToken;
        }
        else if (RuntimeWorkerAuthentication.ValidateManagementToken(
            token[0], Options.Key, Options.PodName, timeProvider.GetUtcNow(), out reason))
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
        if (!Request.Headers.TryGetValue(RuntimeWorkerAuthentication.SiteTokenHeader, out _))
        {
            Log.AuthenticationRejected(Logger, AuthenticationFailureReason.MissingToken);
        }

        return base.HandleChallengeAsync(properties);
    }

    internal sealed class Credentials : AuthenticationSchemeOptions
    {
        public string PodName { get; private set; } = string.Empty;

        public byte[] Key { get; private set; } = [];

        internal void Configure(IConfiguration configuration)
        {
            string? podName = configuration["WEBSITE_POD_NAME"];
            PodName = string.IsNullOrWhiteSpace(podName) ? configuration["CONTAINER_NAME"] ?? string.Empty : podName;
            string? encodedKey = configuration[RuntimeWorkerAuthentication.WorkerKeySetting];
            RuntimeWorkerAuthentication.TryDecodeWorkerKey(encodedKey, out byte[] key);
            Key = key;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Warning, "Container management authentication failed: {Reason}.")]
        public static partial void AuthenticationRejected(ILogger logger, AuthenticationFailureReason reason);
    }
}
