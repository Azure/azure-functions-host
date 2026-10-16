// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace Azure.Functions.WorkerProxy.ManagedIdentity;

internal static class ManagedIdentityEndpoint
{
    internal const string Path = "/msi/token";

    private const string ApiVersionQuery = "api-version";
    private const string ClientIdQuery = "client_id";
    private const string IdentityHeaderEnvironment = "IDENTITY_HEADER";
    private const string IdentityHeaderName = "X-IDENTITY-HEADER";
    private const string LegacyClientIdQuery = "clientid";
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
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (!stateManager.TryGetAssignmentEnvironmentValues(
            MsiSecretEnvironment,
            IdentityHeaderEnvironment,
            out string? msiSecret,
            out string? identityHeader))
        {
            return RetryableFailure(request.HttpContext.Response, "The worker pod assignment is not ready.");
        }

        bool hasMsiSecret = !string.IsNullOrEmpty(msiSecret);
        bool hasIdentityHeader = !string.IsNullOrEmpty(identityHeader);
        if (!hasMsiSecret && !hasIdentityHeader)
        {
            return RetryableFailure(request.HttpContext.Response, "The worker pod assignment has no managed identity credential.");
        }

        if ((!hasMsiSecret || !HeaderMatches(request.Headers, MsiSecretHeaderName, msiSecret!))
            && (!hasIdentityHeader || !HeaderMatches(request.Headers, IdentityHeaderName, identityHeader!)))
        {
            return Error(StatusCodes.Status403Forbidden, "access_denied", "The managed identity secret header is invalid.");
        }

        if (!HasOnlySupportedQueryParameters(request.Query)
            || !TryGetRequiredQueryValue(request.Query, ResourceQuery, out string? resource)
            || !TryGetRequiredQueryValue(request.Query, ApiVersionQuery, out string? apiVersion)
            || !TryGetClientId(request.Query, out string? clientId))
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_request",
                "The request must include one resource and api-version value, at most one clientid or client_id value, and no unsupported parameters.");
        }

        ManagedIdentityTokenResponse response;
        try
        {
            response = await relay.RequestManagedIdentityTokenAsync(
                new ManagedIdentityTokenRequest
                {
                    Resource = resource,
                    ClientId = clientId ?? string.Empty,
                    ApiVersion = apiVersion
                },
                configuration[ManagedIdentityTokenRequestAuthenticator.RuntimeAccessKeyEnvironmentVariable],
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

        HttpResponse httpResponse = request.HttpContext.Response;
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

    private static bool TryGetClientId(IQueryCollection query, out string? value)
    {
        bool hasClientId = query.ContainsKey(ClientIdQuery);
        bool hasLegacyClientId = query.ContainsKey(LegacyClientIdQuery);
        if (hasClientId && hasLegacyClientId)
        {
            value = null;
            return false;
        }

        return TryGetOptionalQueryValue(
            query,
            hasLegacyClientId ? LegacyClientIdQuery : ClientIdQuery,
            out value);
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

internal sealed record ManagedIdentityErrorResponse(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("error_description")] string ErrorDescription);

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ManagedIdentityErrorResponse))]
internal sealed partial class ManagedIdentityJsonContext : JsonSerializerContext;
