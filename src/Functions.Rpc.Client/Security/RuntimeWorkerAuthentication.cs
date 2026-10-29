// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Azure.Functions.Authentication;

/// <summary>
/// Defines the runtime-to-worker JWT contract. This source is also compiled into WorkerProxy.
/// </summary>
internal static class RuntimeWorkerAuthentication
{
    public const string WorkerKeySetting = "CONTAINER_ENCRYPTION_KEY";
    public const string AppKeySetting = "WEBSITE_AUTH_ENCRYPTION_KEY";
    public const string AppNameSetting = "WEBSITE_SITE_NAME";
    public const string WorkerTokenHeader = "x-ms-functions-worker-token";
    public const string AppTokenHeader = "x-ms-functions-app-token";
    public const string AppProofCapability = "WorkerProxyAppToken";
    public const string SiteTokenHeader = "x-ms-site-token";
    public const string ManagementIssuer = "https://legion.core.azurewebsites.net";
    public const string WorkerPurpose = "worker";
    public const string AppPurpose = "app";
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);
    // Match the existing platform bearer validator's default lifetime tolerance.
    public static readonly TimeSpan ManagementClockSkew = TimeSpan.FromMinutes(5);

    private const int MaximumTokenLength = 4096;
    private static readonly string Header = Base64Url.EncodeToString("""{"alg":"HS256","typ":"JWT"}"""u8);

    public static IReadOnlyList<string> TokenHeaders { get; } = [WorkerTokenHeader, AppTokenHeader, SiteTokenHeader];

    public static string WorkerAudience(string workerId) => $"azure-functions-worker:{workerId}";

    public static string AppAudience(string appName) => $"azure-functions-app:{appName.ToLowerInvariant()}";

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

    public static bool ValidateManagementToken(string? token, ReadOnlySpan<byte> key, string podName, DateTimeOffset now)
    {
        if (key.Length < 32 || string.IsNullOrWhiteSpace(podName)
            || string.IsNullOrEmpty(token) || token.Length > MaximumTokenLength)
        {
            return false;
        }

        string[] parts = token.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        try
        {
            byte[] signature = Base64Url.DecodeFromChars(parts[2]);
            byte[] expected = HMACSHA256.HashData(key, Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"));
            if (!CryptographicOperations.FixedTimeEquals(signature, expected))
            {
                return false;
            }

            JsonDocumentOptions options = new() { MaxDepth = 8 };
            using JsonDocument header = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[0]), options);
            using JsonDocument payload = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]), options);
            JsonElement claims = payload.RootElement;
            if (!HasUniqueProperties(header.RootElement) || !HasUniqueProperties(claims)
                || !HasString(header.RootElement, "alg", "HS256")
                || !HasString(header.RootElement, "typ", "JWT")
                || header.RootElement.TryGetProperty("crit", out _)
                || header.RootElement.TryGetProperty("b64", out _)
                || !HasString(claims, "iss", ManagementIssuer, StringComparison.OrdinalIgnoreCase)
                || !HasAudience(claims, podName)
                || !TryGetTimestamp(claims, "exp", out long expires))
            {
                return false;
            }

            long current = now.ToUnixTimeSeconds();
            long skew = (long)ManagementClockSkew.TotalSeconds;
            if (expires < current - skew)
            {
                return false;
            }

            return !claims.TryGetProperty("nbf", out _)
                || (TryGetTimestamp(claims, "nbf", out long notBefore) && notBefore <= expires && notBefore <= current + skew);
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return false;
        }
    }

    public static bool TryDecodeWorkerKey(string? value, out byte[] key) => TryDecodeKey(value, allowHex: true, out key);

    public static bool TryDecodeAppKey(string? value, out byte[] key) => TryDecodeKey(value, allowHex: true, out key);

    public static IReadOnlyDictionary<string, string> CreateHeaders(
        ReadOnlySpan<byte> workerKey, string workerId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [WorkerTokenHeader] = CreateToken(workerKey, WorkerAudience(workerId), WorkerPurpose, now)
        };
    }

    public static bool ValidateHeaders(
        Func<string, string?> readToken, ReadOnlySpan<byte> workerKey, string workerId, DateTimeOffset now)
        => !string.IsNullOrWhiteSpace(workerId)
            && ValidateToken(readToken(WorkerTokenHeader), workerKey, WorkerAudience(workerId), WorkerPurpose, now);

    public static string CreateAppProof(ReadOnlySpan<byte> appKey, string appName, string workerId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        return CreateToken(appKey, AppAudience(appName), AppPurpose, now, workerId);
    }

    public static bool ValidateAppProof(
        string? token, ReadOnlySpan<byte> appKey, string appName, string workerId, DateTimeOffset now)
        => !string.IsNullOrWhiteSpace(appName) && !string.IsNullOrWhiteSpace(workerId)
            && ValidateToken(token, appKey, AppAudience(appName), AppPurpose, now, workerId);

    public static string? RemoveAppProof(IDictionary<string, string> capabilities)
    {
        capabilities.TryGetValue(AppProofCapability, out string? proof);
        capabilities.Remove(AppProofCapability);

        return proof;
    }

    public static void SetAppProof(IDictionary<string, string> capabilities, string proof)
        => capabilities[AppProofCapability] = proof;

    public static string CreateToken(
        ReadOnlySpan<byte> key, string audience, string purpose, DateTimeOffset now, string? workerId = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(key.Length, 32);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("aud", audience);
            writer.WriteString("purpose", purpose);
            if (workerId is not null)
            {
                writer.WriteString("worker_id", workerId);
            }
            writer.WriteNumber("iat", now.ToUnixTimeSeconds());
            writer.WriteNumber("nbf", now.ToUnixTimeSeconds());
            writer.WriteNumber("exp", now.Add(TokenLifetime).ToUnixTimeSeconds());
            writer.WriteEndObject();
        }

        string content = $"{Header}.{Base64Url.EncodeToString(buffer.WrittenSpan)}";
        string signature = Base64Url.EncodeToString(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(content)));

        return $"{content}.{signature}";
    }

    public static bool ValidateToken(
        string? token, ReadOnlySpan<byte> key, string audience, string purpose, DateTimeOffset now, string? workerId = null)
    {
        if (key.Length < 32 || string.IsNullOrEmpty(token) || token.Length > MaximumTokenLength)
        {
            return false;
        }

        string[] parts = token.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        try
        {
            byte[] signature = Base64Url.DecodeFromChars(parts[2]);
            byte[] expectedSignature = HMACSHA256.HashData(key, Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"));
            if (!CryptographicOperations.FixedTimeEquals(signature, expectedSignature))
            {
                return false;
            }

            JsonDocumentOptions options = new() { MaxDepth = 8 };
            using JsonDocument header = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[0]), options);
            using JsonDocument payload = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]), options);
            JsonElement claims = payload.RootElement;
            if (!HasUniqueProperties(header.RootElement) || !HasUniqueProperties(claims)
                || !HasString(header.RootElement, "alg", "HS256")
                || !HasString(header.RootElement, "typ", "JWT")
                || header.RootElement.TryGetProperty("crit", out _)
                || header.RootElement.TryGetProperty("b64", out _)
                || !HasString(claims, "aud", audience)
                || !HasString(claims, "purpose", purpose)
                || (workerId is not null && !HasString(claims, "worker_id", workerId))
                || !TryGetTimestamp(claims, "iat", out long issued)
                || !TryGetTimestamp(claims, "nbf", out long notBefore)
                || !TryGetTimestamp(claims, "exp", out long expires))
            {
                return false;
            }

            long current = now.ToUnixTimeSeconds();
            long skew = (long)ClockSkew.TotalSeconds;

            return issued >= 0 && notBefore == issued && expires > issued
                && expires - issued <= (long)TokenLifetime.TotalSeconds
                && issued <= current + skew && expires > current - skew;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return false;
        }
    }

    private static bool TryDecodeKey(string? value, bool allowHex, out byte[] key)
    {
        key = [];
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumTokenLength)
        {
            return false;
        }

        try
        {
            // Match the platform's website-key convention: 64 characters denotes a 256-bit hex key.
            byte[] decoded = allowHex && value.Length == 64 ? Convert.FromHexString(value) : Convert.FromBase64String(value);
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
