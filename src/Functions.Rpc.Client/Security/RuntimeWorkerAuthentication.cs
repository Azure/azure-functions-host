// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Azure.Functions.Authentication;

/// <summary>
/// Safe authentication failure categories, without token, key or untrusted claim values.
/// </summary>
public enum AuthenticationFailureReason
{
    /// <summary>No failure occurred.</summary>
    None,
    /// <summary>The required key is absent.</summary>
    MissingKey,
    /// <summary>The configured key is invalid.</summary>
    InvalidKey,
    /// <summary>The expected trusted identity is absent.</summary>
    MissingIdentity,
    /// <summary>The required token is absent.</summary>
    MissingToken,
    /// <summary>The token or its carrier is malformed.</summary>
    MalformedToken,
    /// <summary>The signature is invalid.</summary>
    Signature,
    /// <summary>The signing algorithm is unsupported.</summary>
    Algorithm,
    /// <summary>The issuer is invalid.</summary>
    Issuer,
    /// <summary>The audience is invalid.</summary>
    Audience,
    /// <summary>The directional purpose is invalid.</summary>
    Purpose,
    /// <summary>The worker binding is invalid.</summary>
    WorkerIdentity,
    /// <summary>The token lifetime is invalid.</summary>
    Lifetime,
    /// <summary>No accepted assignment is available.</summary>
    AssignmentUnavailable,
    /// <summary>The request did not use the required transport.</summary>
    Transport
}

/// <summary>
/// Defines the runtime-to-worker JWT contract. This source is also compiled into WorkerProxy.
/// </summary>
public static class RuntimeWorkerAuthentication
{
    /// <summary>The receiving container's management and worker-caller key setting.</summary>
    public const string WorkerKeySetting = "CONTAINER_ENCRYPTION_KEY";
    /// <summary>The application proof key setting.</summary>
    public const string AppKeySetting = "WEBSITE_AUTH_ENCRYPTION_KEY";
    /// <summary>The trusted application identity setting.</summary>
    public const string AppNameSetting = "WEBSITE_SITE_NAME";
    /// <summary>The runtime-to-worker caller token header.</summary>
    public const string WorkerTokenHeader = "x-ms-functions-worker-token";
    /// <summary>The reserved legacy app-token header, removed before customer forwarding.</summary>
    public const string AppTokenHeader = "x-ms-functions-app-token";
    /// <summary>The reserved worker-to-runtime proof capability.</summary>
    public const string AppProofCapability = "WorkerProxyAppToken";
    /// <summary>The container management token header.</summary>
    public const string SiteTokenHeader = "x-ms-site-token";
    /// <summary>The expected platform management issuer.</summary>
    public const string ManagementIssuer = "https://legion.core.azurewebsites.net";
    /// <summary>The worker-caller purpose.</summary>
    public const string WorkerPurpose = "worker";
    /// <summary>The app-proof purpose.</summary>
    public const string AppPurpose = "app";
    /// <summary>The maximum directional token lifetime.</summary>
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);
    /// <summary>The directional token clock tolerance.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);
    /// <summary>The existing platform management lifetime tolerance.</summary>
    public static readonly TimeSpan ManagementClockSkew = TimeSpan.FromMinutes(5);

    private const int MaximumTokenLength = 4096;
    private static readonly JsonWebTokenHandler TokenHandler = new()
    {
        MaximumTokenSizeInBytes = MaximumTokenLength,
        SetDefaultTimesOnTokenCreation = false
    };

    private static readonly CryptoProviderFactory CryptoProvider = new() { CacheSignatureProviders = false };

    /// <summary>Reserved platform headers that must not reach the customer.</summary>
    public static IReadOnlyList<string> TokenHeaders { get; } = [WorkerTokenHeader, AppTokenHeader, SiteTokenHeader];

    /// <summary>Returns the exact worker-caller audience.</summary>
    public static string WorkerAudience(string workerId) => $"azure-functions-worker:{workerId}";

    /// <summary>Returns the application audience after trusted-name normalization.</summary>
    public static string AppAudience(string appName) => $"azure-functions-app:{appName.ToLowerInvariant()}";

    /// <summary>Parses an allocated HTTPS origin or explicit scheme-less host and port.</summary>
    public static bool TryCreateHttpsOrigin(string? value, [NotNullWhen(true)] out Uri? endpoint)
    {
        endpoint = null;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (char character in value)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                return false;
            }
        }

        if (!value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (value.AsSpan().IndexOfAny("/\\@?#%".AsSpan()) >= 0)
            {
                return false;
            }

            int separator = value.LastIndexOf(':');
            if (separator <= 0
                || !int.TryParse(value.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int port)
                || port is < 1 or > ushort.MaxValue)
            {
                return false;
            }

            value = $"https://{value}";
        }

        return Uri.TryCreate(value, UriKind.Absolute, out endpoint)
            && string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(endpoint.Host)
            && endpoint.Port > 0
            && string.IsNullOrEmpty(endpoint.UserInfo)
            && string.Equals(endpoint.AbsolutePath, "/", StringComparison.Ordinal)
            && string.IsNullOrEmpty(endpoint.Query)
            && string.IsNullOrEmpty(endpoint.Fragment)
            && !value.Contains('\\', StringComparison.Ordinal)
            && !value.Contains('%', StringComparison.Ordinal);
    }

    /// <summary>Validates the receiving container's platform management token.</summary>
    public static bool ValidateManagementToken(
        string? token,
        ReadOnlySpan<byte> key,
        string podName,
        DateTimeOffset now)
        => ValidateManagementToken(token, key, podName, now, out _);

    /// <summary>Validates a platform management token and reports a safe failure category.</summary>
    public static bool ValidateManagementToken(
        string? token,
        ReadOnlySpan<byte> key,
        string podName,
        DateTimeOffset now,
        out AuthenticationFailureReason reason)
    {
        if (string.IsNullOrWhiteSpace(podName))
        {
            return Reject(AuthenticationFailureReason.MissingIdentity, out reason);
        }

        if (!TryValidateToken(token, key, podName, management: true, now, out JsonDocument? payload, out reason))
        {
            return false;
        }

        using (payload)
        {
            return true;
        }
    }

    /// <summary>Decodes a platform container key with a minimum size of 256 bits.</summary>
    public static bool TryDecodeWorkerKey(string? value, out byte[] key) => TryDecodeKey(value, out key);

    /// <summary>Decodes a platform application key with a minimum size of 256 bits.</summary>
    public static bool TryDecodeAppKey(string? value, out byte[] key) => TryDecodeKey(value, out key);

    /// <summary>Creates the single selected-worker caller header.</summary>
    public static IReadOnlyDictionary<string, string> CreateHeaders(
        ReadOnlySpan<byte> workerKey,
        string workerId,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [WorkerTokenHeader] = CreateToken(workerKey, WorkerAudience(workerId), WorkerPurpose, now)
        };
    }

    /// <summary>Signs an application-key proof bound to the expected worker.</summary>
    public static string CreateAppProof(
        ReadOnlySpan<byte> appKey,
        string appName,
        string workerId,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        return CreateToken(appKey, AppAudience(appName), AppPurpose, now, workerId);
    }

    /// <summary>Validates application-key possession and exact worker binding.</summary>
    public static bool ValidateAppProof(
        string? token,
        ReadOnlySpan<byte> appKey,
        string appName,
        string workerId,
        DateTimeOffset now)
        => ValidateAppProof(token, appKey, appName, workerId, now, out _);

    /// <summary>Validates an app proof and reports a safe failure category.</summary>
    public static bool ValidateAppProof(
        string? token,
        ReadOnlySpan<byte> appKey,
        string appName,
        string workerId,
        DateTimeOffset now,
        out AuthenticationFailureReason reason)
    {
        if (string.IsNullOrWhiteSpace(appName) || string.IsNullOrWhiteSpace(workerId))
        {
            return Reject(AuthenticationFailureReason.MissingIdentity, out reason);
        }

        return ValidateToken(token, appKey, AppAudience(appName), AppPurpose, now, out reason, workerId);
    }

    /// <summary>Removes and returns the private proof before normal capability processing.</summary>
    public static string? RemoveAppProof(IDictionary<string, string> capabilities)
    {
        capabilities.TryGetValue(AppProofCapability, out string? proof);
        capabilities.Remove(AppProofCapability);

        return proof;
    }

    /// <summary>Sets the proof on the runtime-facing transport capability map.</summary>
    public static void SetAppProof(IDictionary<string, string> capabilities, string proof)
        => capabilities[AppProofCapability] = proof;

    /// <summary>Creates a bounded HS256 directional token using JsonWebTokens.</summary>
    public static string CreateToken(
        ReadOnlySpan<byte> key,
        string audience,
        string purpose,
        DateTimeOffset now,
        string? workerId = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(key.Length, 32);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        Dictionary<string, object> claims = new(StringComparer.Ordinal) { ["purpose"] = purpose };
        if (workerId is not null)
        {
            claims["worker_id"] = workerId;
        }

        return TokenHandler.CreateToken(new SecurityTokenDescriptor
        {
            Audience = audience,
            Claims = claims,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.Add(TokenLifetime).UtcDateTime,
            TokenType = "JWT",
            SigningCredentials = new(CreateKey(key), SecurityAlgorithms.HmacSha256)
        });
    }

    /// <summary>Validates a directional token's audience, purpose, binding and bounded lifetime.</summary>
    public static bool ValidateToken(
        string? token,
        ReadOnlySpan<byte> key,
        string audience,
        string purpose,
        DateTimeOffset now,
        string? workerId = null)
        => ValidateToken(token, key, audience, purpose, now, out _, workerId);

    /// <summary>Validates a directional token and reports a safe failure category.</summary>
    public static bool ValidateToken(
        string? token,
        ReadOnlySpan<byte> key,
        string audience,
        string purpose,
        DateTimeOffset now,
        out AuthenticationFailureReason reason,
        string? workerId = null)
    {
        if (!TryValidateToken(token, key, audience, management: false, now, out JsonDocument? payload, out reason))
        {
            return false;
        }

        using (payload)
        {
            JsonElement claims = payload.RootElement;

            if (!HasString(claims, "purpose", purpose))
            {
                return Reject(AuthenticationFailureReason.Purpose, out reason);
            }

            if (workerId is not null && !HasString(claims, "worker_id", workerId))
            {
                return Reject(AuthenticationFailureReason.WorkerIdentity, out reason);
            }

            reason = AuthenticationFailureReason.None;

            return true;
        }
    }

    private static bool TryValidateToken(
        string? token,
        ReadOnlySpan<byte> key,
        string audience,
        bool management,
        DateTimeOffset now,
        [NotNullWhen(true)] out JsonDocument? payload,
        out AuthenticationFailureReason reason)
    {
        payload = null;
        if (key.Length < 32)
        {
            return Reject(AuthenticationFailureReason.InvalidKey, out reason);
        }

        if (string.IsNullOrEmpty(token))
        {
            return Reject(AuthenticationFailureReason.MissingToken, out reason);
        }

        if (token.Length > MaximumTokenLength)
        {
            return Reject(AuthenticationFailureReason.MalformedToken, out reason);
        }

        string[] parts = token.Split('.');
        if (parts.Length != 3)
        {
            return Reject(AuthenticationFailureReason.MalformedToken, out reason);
        }

        JsonDocument? parsedPayload = null;
        try
        {
            JsonDocumentOptions options = new() { MaxDepth = 8 };
            using JsonDocument header = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[0]), options);
            parsedPayload = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]), options);
            if (!HasUniqueProperties(header.RootElement) || !HasUniqueProperties(parsedPayload.RootElement)
                || !HasString(header.RootElement, "typ", "JWT")
                || header.RootElement.TryGetProperty("crit", out _)
                || header.RootElement.TryGetProperty("b64", out _))
            {
                return Reject(AuthenticationFailureReason.MalformedToken, out reason);
            }

            if (!HasString(header.RootElement, "alg", "HS256"))
            {
                return Reject(AuthenticationFailureReason.Algorithm, out reason);
            }

            JsonElement claims = parsedPayload.RootElement;
            TokenValidationParameters validation = new()
            {
                IssuerSigningKey = CreateKey(key),
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ValidTypes = ["JWT"],
                ValidateIssuer = management,
                IssuerValidator = management ? (issuer, _, _) =>
                    string.Equals(issuer, ManagementIssuer, StringComparison.OrdinalIgnoreCase)
                        ? issuer : throw new SecurityTokenInvalidIssuerException() : null,
                ValidateAudience = true,
                AudienceValidator = (_, _, _) => management ? HasAudience(claims, audience) : HasString(claims, "aud", audience),
                ValidateLifetime = true,
                RequireExpirationTime = true,
                LifetimeValidator = (_, _, _, _) => HasValidLifetime(claims, now, management),
                ClockSkew = management ? ManagementClockSkew : ClockSkew,
                IncludeTokenOnFailedValidation = false,
                LogTokenId = false,
                LogValidationExceptions = false
            };
            // All validation inputs are local: no discovery, metadata retrieval or async application callbacks.
            TokenValidationResult result = TokenHandler.ValidateTokenAsync(token, validation).GetAwaiter().GetResult();
            if (!result.IsValid)
            {
                return Reject(result.Exception switch
                {
                    SecurityTokenInvalidIssuerException => AuthenticationFailureReason.Issuer,
                    SecurityTokenInvalidAudienceException => AuthenticationFailureReason.Audience,
                    SecurityTokenInvalidLifetimeException or SecurityTokenExpiredException or SecurityTokenNotYetValidException
                        or SecurityTokenNoExpirationException => AuthenticationFailureReason.Lifetime,
                    SecurityTokenInvalidSignatureException or SecurityTokenSignatureKeyNotFoundException => AuthenticationFailureReason.Signature,
                    SecurityTokenInvalidAlgorithmException => AuthenticationFailureReason.Algorithm,
                    _ => AuthenticationFailureReason.MalformedToken
                }, out reason);
            }

            payload = parsedPayload;
            parsedPayload = null;
            reason = AuthenticationFailureReason.None;

            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return Reject(AuthenticationFailureReason.MalformedToken, out reason);
        }
        finally
        {
            parsedPayload?.Dispose();
        }
    }

    private static SymmetricSecurityKey CreateKey(ReadOnlySpan<byte> key)
        => new(key.ToArray()) { CryptoProviderFactory = CryptoProvider };

    private static bool HasValidLifetime(JsonElement claims, DateTimeOffset now, bool management)
    {
        if (!TryGetTimestamp(claims, "exp", out long expires))
        {
            return false;
        }

        long current = now.ToUnixTimeSeconds();
        if (management)
        {
            long managementSkew = (long)ManagementClockSkew.TotalSeconds;

            return expires >= current - managementSkew
                && (!claims.TryGetProperty("nbf", out _)
                    || (TryGetTimestamp(claims, "nbf", out long notBefore)
                        && notBefore <= expires && notBefore <= current + managementSkew));
        }

        long skew = (long)ClockSkew.TotalSeconds;

        return TryGetTimestamp(claims, "iat", out long issued)
            && TryGetTimestamp(claims, "nbf", out long starts)
            && issued >= 0 && starts == issued && expires > issued
            && expires - issued <= (long)TokenLifetime.TotalSeconds
            && issued <= current + skew && expires > current - skew;
    }

    private static bool Reject(AuthenticationFailureReason failure, out AuthenticationFailureReason reason)
    {
        reason = failure;

        return false;
    }

    private static bool TryDecodeKey(string? value, out byte[] key)
    {
        key = [];
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumTokenLength)
        {
            return false;
        }

        try
        {
            // Match the platform's website-key convention: 64 characters denotes a 256-bit hex key.
            byte[] decoded = value.Length == 64 ? Convert.FromHexString(value) : Convert.FromBase64String(value);
            if (decoded.Length < 32)
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

    private static bool HasUniqueProperties(JsonElement element)
    {
        if (element.ValueKind is not JsonValueKind.Object)
        {
            return false;
        }

        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasString(JsonElement element, string name, string expected, StringComparison comparison = StringComparison.Ordinal)
        => element.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.String
            && string.Equals(value.GetString(), expected, comparison);

    private static bool HasAudience(JsonElement claims, string expected)
    {
        if (!claims.TryGetProperty("aud", out JsonElement audience))
        {
            return false;
        }

        if (audience.ValueKind is JsonValueKind.String)
        {
            return string.Equals(audience.GetString(), expected, StringComparison.OrdinalIgnoreCase);
        }

        if (audience.ValueKind is JsonValueKind.Array)
        {
            foreach (JsonElement value in audience.EnumerateArray())
            {
                if (value.ValueKind is JsonValueKind.String
                    && string.Equals(value.GetString(), expected, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryGetTimestamp(JsonElement element, string name, out long timestamp)
    {
        timestamp = 0;

        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.Number
            && value.TryGetInt64(out timestamp);
    }
}
