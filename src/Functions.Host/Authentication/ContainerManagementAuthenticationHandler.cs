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
internal sealed class ContainerManagementAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    ContainerManagementAuthenticationHandler.Credentials credentials,
    TimeProvider timeProvider) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    internal const string SchemeName = "ContainerManagement";

    internal static void AddServices(IServiceCollection services)
    {
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<Credentials>();
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, ContainerManagementAuthenticationHandler>(SchemeName, static _ => { });
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

        if (Request.Headers[RuntimeWorkerAuthentication.SiteTokenHeader] is [string token]
            && RuntimeWorkerAuthentication.ValidateManagementToken(token, credentials.Key, credentials.PodName, timeProvider.GetUtcNow()))
        {
            ClaimsIdentity identity = new([new Claim(ClaimTypes.NameIdentifier, credentials.PodName)], Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(identity), Scheme.Name)));
        }

        Logger.LogWarning("Container management authentication failed.");

        return Task.FromResult(AuthenticateResult.Fail("Container management authentication failed."));
    }

    internal sealed class Credentials
    {
        public Credentials(IConfiguration configuration)
        {
            string? podName = configuration["WEBSITE_POD_NAME"];
            PodName = string.IsNullOrWhiteSpace(podName) ? configuration["CONTAINER_NAME"] ?? string.Empty : podName;
            RuntimeWorkerAuthentication.TryDecodeWorkerKey(configuration[RuntimeWorkerAuthentication.WorkerKeySetting], out byte[] key);
            Key = key;
        }

        public string PodName { get; }

        public byte[] Key { get; }
    }
}
