// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Azure.Functions.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.WorkerProxy.Authentication;

/// <summary>
/// Authenticates runtime calls through the named ASP.NET Core scheme without changing customer Authorization headers.
/// </summary>
internal sealed partial class RuntimeRequestAuthenticator(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IRuntimeAuthenticationKeyProvider keyProvider,
    TimeProvider timeProvider) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    internal const string SchemeName = "WorkerProxyRuntime";

    internal static void AddServices(IServiceCollection services)
    {
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, RuntimeRequestAuthenticator>(SchemeName, static _ => { });
        services.AddAuthorization(options => options.AddPolicy(SchemeName,
            policy => policy.AddAuthenticationSchemes(SchemeName).RequireAuthenticatedUser()));
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(RuntimeWorkerAuthentication.WorkerTokenHeader, out var token))
        {
            return Rejected(AuthenticationFailureReason.MissingToken);
        }

        RuntimeWorkerAuthenticationKeys? keys = keyProvider.GetWorkerKeys();
        if (keys is null)
        {
            return Rejected(AuthenticationFailureReason.AssignmentUnavailable);
        }

        if (!Request.IsHttps)
        {
            return Rejected(AuthenticationFailureReason.Transport);
        }

        AuthenticationFailureReason reason;
        if (token is not [string] || token.ToString().Contains(',', StringComparison.Ordinal))
        {
            reason = AuthenticationFailureReason.MalformedToken;
        }
        else if (RuntimeWorkerAuthentication.ValidateToken(
            token[0], keys.WorkerKey.Span, RuntimeWorkerAuthentication.WorkerAudience(keys.WorkerId),
            RuntimeWorkerAuthentication.WorkerPurpose, timeProvider.GetUtcNow(), out reason))
        {
            foreach (string name in RuntimeWorkerAuthentication.TokenHeaders)
            {
                Request.Headers.Remove(name);
            }

            ClaimsIdentity identity = new([new Claim(ClaimTypes.NameIdentifier, keys.WorkerId)], Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(identity), Scheme.Name)));
        }

        return Rejected(reason);
    }

    private Task<AuthenticateResult> Rejected(AuthenticationFailureReason reason)
    {
        Log.AuthenticationRejected(Logger, reason);

        return Task.FromResult(AuthenticateResult.Fail("Runtime authentication failed."));
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Warning, "Runtime request authentication failed: {Reason}.")]
        public static partial void AuthenticationRejected(ILogger logger, AuthenticationFailureReason reason);
    }
}
