// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
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
    public const string WorkerPurpose = "worker";
    public const string AppPurpose = "app";
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    private const int MaximumTokenLength = 4096;
    private static readonly string Header = Base64Url.EncodeToString("""{"alg":"HS256","typ":"JWT"}"""u8);

    public static IReadOnlyList<string> TokenHeaders { get; } = [WorkerTokenHeader, AppTokenHeader];

    public static string WorkerAudience(string workerId) => $"azure-functions-worker:{workerId}";

    public static string AppAudience(string appName) => $"azure-functions-app:{appName}";

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

    private static bool HasString(JsonElement element, string name, string expected)
        => element.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.String
            && string.Equals(value.GetString(), expected, StringComparison.Ordinal);

    private static bool TryGetTimestamp(JsonElement element, string name, out long timestamp)
    {
        timestamp = 0;

        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.Number
            && value.TryGetInt64(out timestamp);
    }
}
