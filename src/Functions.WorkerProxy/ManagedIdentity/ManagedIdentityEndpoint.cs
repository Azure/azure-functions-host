// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Primitives;

namespace Azure.Functions.WorkerProxy.ManagedIdentity;

internal static class ManagedIdentityEndpoint
{
    internal const string Path = "/msi/token";

    private const string ApiVersionQuery = "api-version";
    private const string ClientIdQuery = "client_id";
    private const string CurrentMsiApiVersion = "2019-08-01";
    private const string IdentityHeaderName = "X-IDENTITY-HEADER";
    private const string LegacyClientIdQuery = "clientid";
    private const string LegacyMsiApiVersion = "2017-09-01";
    private const string LegacyMsiApiVersionAlias = "0.1";
    private const string MsiSecretEnvironment = "MSI_SECRET";
    private const string MsiSecretHeaderName = "Secret";
    private const string ResourceQuery = "resource";
    private static readonly HashSet<string> SupportedQueryParameters =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ApiVersionQuery,
            ClientIdQuery,
            LegacyClientIdQuery,
            ResourceQuery
        };

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Path, GetTokenAsync).AllowAnonymous();
    }

    private static async Task<IResult> GetTokenAsync(
        HttpRequest request,
        WorkerPodStateManager stateManager,
        FunctionRpcRelay relay,
        ManagedIdentityTokenCache tokenCache,
        CancellationToken cancellationToken)
    {
        if (!stateManager.TryGetAssignmentEnvironmentValue(MsiSecretEnvironment, out string? msiSecret))
        {
            return RetryableFailure(request.HttpContext.Response, "The worker pod assignment is not ready.");
        }

        if (!TryGetRequiredQueryValue(request.Query, ApiVersionQuery, out string? apiVersion)
            || apiVersion is null
            || !TryGetManagedIdentityContract(apiVersion, out bool usesLegacyContract))
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_request",
                "The request must specify a supported api-version.");
        }

        string expectedHeaderName = usesLegacyContract ? MsiSecretHeaderName : IdentityHeaderName;
        if (string.IsNullOrEmpty(msiSecret))
        {
            return RetryableFailure(request.HttpContext.Response,
                "The worker pod assignment has no credential for the requested managed identity API version.");
        }

        if (!HeaderMatches(request.Headers, expectedHeaderName, msiSecret))
        {
            return Error(StatusCodes.Status403Forbidden, "access_denied", "The managed identity secret header is invalid.");
        }

        if (!HasOnlySupportedQueryParameters(request.Query)
            || !TryGetRequiredQueryValue(request.Query, ResourceQuery, out string? resource)
            || !TryGetClientId(request.Query, usesLegacyContract, out string? clientId))
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_request",
                "The request must include one resource value, use the client ID selector for its api-version at most once, and include no unsupported parameters.");
        }

        ManagedIdentityTokenRequest tokenRequest = new()
        {
            Resource = resource,
            ClientId = clientId ?? string.Empty,
            ApiVersion = apiVersion
        };
        if (relay.TryGetActiveSessionId(FunctionRpcRelaySide.Runtime, out long cachedSessionId)
            && tokenCache.Get(cachedSessionId, tokenRequest) is ManagedIdentityTokenResponse cachedResponse
            && relay.TryGetActiveSessionId(FunctionRpcRelaySide.Runtime, out long confirmedSessionId)
            && confirmedSessionId == cachedSessionId)
        {
            return await WriteResponseAsync(request.HttpContext.Response, cachedResponse, cancellationToken);
        }

        long responseSessionId;
        ManagedIdentityTokenResponse response;
        try
        {
            (responseSessionId, response) = await relay.RequestManagedIdentityTokenAsync(
                tokenRequest,
                cancellationToken);
        }
        catch (ManagedIdentityTokenUnavailableException exception)
        {
            return RetryableFailure(request.HttpContext.Response, exception.Message);
        }
        catch (TimeoutException)
        {
            return RetryableFailure(request.HttpContext.Response, "The runtime token request timed out.");
        }

        if (response.Result?.Status != StatusResult.Types.Status.Success
            || response.StatusCode is < StatusCodes.Status100Continue or > 599)
        {
            string detail = response.Result?.Exception?.Message
                ?? response.Result?.Result
                ?? "The runtime rejected the managed identity token request.";
            return RetryableFailure(request.HttpContext.Response, detail);
        }

        if (relay.TryGetActiveSessionId(FunctionRpcRelaySide.Runtime, out long activeSessionId)
            && activeSessionId == responseSessionId)
        {
            tokenCache.Store(responseSessionId, tokenRequest, response);
        }

        return await WriteResponseAsync(request.HttpContext.Response, response, cancellationToken);
    }

    private static async Task<IResult> WriteResponseAsync(
        HttpResponse httpResponse,
        ManagedIdentityTokenResponse response,
        CancellationToken cancellationToken)
    {
        httpResponse.StatusCode = response.StatusCode;
        if (!string.IsNullOrWhiteSpace(response.ContentType))
        {
            httpResponse.ContentType = response.ContentType;
        }

        byte[] body = response.Body.ToByteArray();
        httpResponse.ContentLength = body.Length;
        await httpResponse.Body.WriteAsync(body, cancellationToken);
        return Results.Empty;
    }

    private static bool HeaderMatches(IHeaderDictionary headers, string name, string expected)
    {
        if (!headers.TryGetValue(name, out StringValues values) || values is not [string actual])
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actual),
            Encoding.UTF8.GetBytes(expected));
    }

    private static bool HasOnlySupportedQueryParameters(IQueryCollection query)
    {
        foreach (string key in query.Keys)
        {
            if (!SupportedQueryParameters.Contains(key))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetRequiredQueryValue(IQueryCollection query, string name, out string? value)
    {
        if (query.TryGetValue(name, out StringValues values) && values is [string item] && !string.IsNullOrWhiteSpace(item))
        {
            value = item;
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryGetOptionalQueryValue(IQueryCollection query, string name, out string? value)
    {
        if (!query.TryGetValue(name, out StringValues values))
        {
            value = null;
            return true;
        }

        if (values is [string item] && !string.IsNullOrWhiteSpace(item))
        {
            value = item;
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryGetClientId(IQueryCollection query, bool usesLegacyContract, out string? value)
    {
        string expectedName = usesLegacyContract ? LegacyClientIdQuery : ClientIdQuery;
        string unsupportedName = usesLegacyContract ? ClientIdQuery : LegacyClientIdQuery;
        if (query.ContainsKey(unsupportedName))
        {
            value = null;
            return false;
        }

        return TryGetOptionalQueryValue(query, expectedName, out value);
    }

    private static bool TryGetManagedIdentityContract(string apiVersion, out bool usesLegacyContract)
    {
        usesLegacyContract = string.Equals(apiVersion, LegacyMsiApiVersion, StringComparison.Ordinal)
            || string.Equals(apiVersion, LegacyMsiApiVersionAlias, StringComparison.Ordinal);
        return usesLegacyContract
            || string.Equals(apiVersion, CurrentMsiApiVersion, StringComparison.Ordinal);
    }

    private static IResult RetryableFailure(HttpResponse response, string detail)
    {
        response.Headers.RetryAfter = "1";
        return Error(StatusCodes.Status503ServiceUnavailable, "temporarily_unavailable", detail);
    }

    private static IResult Error(int statusCode, string error, string detail) =>
        TypedResults.Json(
            new ManagedIdentityErrorResponse(error, detail),
            ManagedIdentityJsonContext.Default.ManagedIdentityErrorResponse,
            statusCode: statusCode);
}

internal sealed class ManagedIdentityTokenCache(TimeProvider timeProvider)
{
    private const int MaximumCachedResponseSize = 64 * 1024;
    private const int MaximumEntries = 32;
    private static readonly TimeSpan MaximumCacheLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromMinutes(1);
    private readonly Dictionary<CacheKey, CacheEntry> _entries = [];
    private readonly Lock _lock = new();
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private long _runtimeSessionId;

    public ManagedIdentityTokenResponse? Get(
        long runtimeSessionId,
        ManagedIdentityTokenRequest request)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(runtimeSessionId);
        ArgumentNullException.ThrowIfNull(request);

        CacheKey key = new(request.Resource, request.ClientId, request.ApiVersion);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        lock (_lock)
        {
            if (!SelectSessionLocked(runtimeSessionId))
            {
                return null;
            }

            if (_entries.TryGetValue(key, out CacheEntry? entry))
            {
                if (entry.CacheUntil > now)
                {
                    return entry.Response.Clone();
                }

                _entries.Remove(key);
            }
        }

        return null;
    }

    public void Store(
        long runtimeSessionId,
        ManagedIdentityTokenRequest request,
        ManagedIdentityTokenResponse response)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(runtimeSessionId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);

        if (response.StatusCode != StatusCodes.Status200OK
            || response.Result?.Status != StatusResult.Types.Status.Success
            || response.Body.IsEmpty
            || response.CalculateSize() > MaximumCachedResponseSize
            || !TryGetExpiration(response.Body.ToByteArray(), out DateTimeOffset tokenExpiration))
        {
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (tokenExpiration <= now + RefreshBuffer)
        {
            return;
        }

        DateTimeOffset cacheUntil = tokenExpiration - RefreshBuffer;
        if (cacheUntil > now + MaximumCacheLifetime)
        {
            cacheUntil = now + MaximumCacheLifetime;
        }

        CacheKey key = new(request.Resource, request.ClientId, request.ApiVersion);
        CacheEntry entry = new(response.Clone(), cacheUntil);
        lock (_lock)
        {
            if (!SelectSessionLocked(runtimeSessionId))
            {
                return;
            }

            List<CacheKey>? expiredKeys = null;
            foreach ((CacheKey expiredKey, CacheEntry expiredEntry) in _entries)
            {
                if (expiredEntry.CacheUntil <= now)
                {
                    (expiredKeys ??= []).Add(expiredKey);
                }
            }

            if (expiredKeys is not null)
            {
                foreach (CacheKey expiredKey in expiredKeys)
                {
                    _entries.Remove(expiredKey);
                }
            }

            if (_entries.ContainsKey(key) || _entries.Count < MaximumEntries)
            {
                _entries[key] = entry;
            }
        }
    }

    private bool SelectSessionLocked(long runtimeSessionId)
    {
        if (runtimeSessionId < _runtimeSessionId)
        {
            return false;
        }

        if (runtimeSessionId > _runtimeSessionId)
        {
            _entries.Clear();
            _runtimeSessionId = runtimeSessionId;
        }

        return true;
    }

    private static bool TryGetExpiration(
        ReadOnlyMemory<byte> responseBody,
        out DateTimeOffset expiration)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("access_token", out JsonElement accessToken)
                || accessToken.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(accessToken.GetString())
                || !root.TryGetProperty("expires_on", out JsonElement expiresOn))
            {
                expiration = default;
                return false;
            }

            string? value = expiresOn.ValueKind switch
            {
                JsonValueKind.String => expiresOn.GetString(),
                JsonValueKind.Number => expiresOn.GetRawText(),
                _ => null
            };
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long unixTimeSeconds))
            {
                try
                {
                    expiration = DateTimeOffset.FromUnixTimeSeconds(unixTimeSeconds);
                    return true;
                }
                catch (ArgumentOutOfRangeException)
                {
                }
            }

            return DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out expiration);
        }
        catch (JsonException)
        {
            expiration = default;
            return false;
        }
    }

    private readonly record struct CacheKey(
        string Resource,
        string ClientId,
        string ApiVersion);

    private sealed record CacheEntry(
        ManagedIdentityTokenResponse Response,
        DateTimeOffset CacheUntil);
}

internal sealed record ManagedIdentityErrorResponse(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("error_description")] string ErrorDescription);

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ManagedIdentityErrorResponse))]
internal sealed partial class ManagedIdentityJsonContext : JsonSerializerContext;
