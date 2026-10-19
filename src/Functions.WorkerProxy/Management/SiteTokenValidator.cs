// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Azure.Functions.WorkerProxy.Management;

internal sealed partial class SiteTokenValidator
{
    internal const string HeaderName = "x-ms-site-token";
    private const string ContainerEncryptionKey = "CONTAINER_ENCRYPTION_KEY";
    private const string AppServiceCoreIssuer = "https://appservice.core.azurewebsites.net";
    private const string LegionCoreIssuer = "https://legion.core.azurewebsites.net";
    private readonly JwtSecurityTokenHandler _tokenHandler = new();
    private readonly TokenValidationParameters? _validationParameters;

    public SiteTokenValidator(
        IConfiguration configuration,
        IOptions<WorkerProxyOptions> options,
        ILogger<SiteTokenValidator> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        string podName = options.Value.PodName;
        string? configuredKey = configuration[ContainerEncryptionKey];
        if (!TryDecodeKey(configuredKey, out byte[]? key))
        {
            Log.InvalidSigningKey(logger);
            return;
        }

        _validationParameters = new()
        {
            AudienceValidator = (audiences, _, _) =>
                audiences.Any(audience => string.Equals(audience, podName, StringComparison.OrdinalIgnoreCase)),
            ClockSkew = TimeSpan.Zero,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            IssuerValidator = ValidateIssuer,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateAudience = true,
            ValidateIssuer = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
        };
    }

    public bool IsValid(string? token)
    {
        if (_validationParameters is null || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            _tokenHandler.ValidateToken(token, _validationParameters, out _);
            return true;
        }
        catch (Exception exception) when (exception is SecurityTokenException or ArgumentException)
        {
            return false;
        }
    }

    private static string ValidateIssuer(
        string issuer,
        SecurityToken token,
        TokenValidationParameters parameters)
    {
        if (string.Equals(issuer, AppServiceCoreIssuer, StringComparison.OrdinalIgnoreCase)
            || string.Equals(issuer, LegionCoreIssuer, StringComparison.OrdinalIgnoreCase))
        {
            return issuer;
        }

        throw new SecurityTokenInvalidIssuerException("The site token issuer is invalid.")
        {
            InvalidIssuer = issuer
        };
    }

    private static bool TryDecodeKey(string? configuredKey, out byte[]? key)
    {
        key = null;
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            return false;
        }

        try
        {
            byte[] decoded = configuredKey.Length == 64 && configuredKey.All(Uri.IsHexDigit)
                ? Convert.FromHexString(configuredKey)
                : Convert.FromBase64String(configuredKey);
            if (decoded.Length != 32)
            {
                return false;
            }

            key = decoded;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(1, LogLevel.Error, "WorkerProxy site-token authentication is unavailable because CONTAINER_ENCRYPTION_KEY is missing or invalid.")]
        public static partial void InvalidSigningKey(ILogger logger);
    }
}
