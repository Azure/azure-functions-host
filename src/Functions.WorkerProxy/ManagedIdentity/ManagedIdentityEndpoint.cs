// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using RpcTokenRequest = Microsoft.Azure.WebJobs.Script.Grpc.Messages.ManagedIdentityTokenRequest;
using RpcTokenResponse = Microsoft.Azure.WebJobs.Script.Grpc.Messages.ManagedIdentityTokenResponse;
using StatusResult = Microsoft.Azure.WebJobs.Script.Grpc.Messages.StatusResult;

namespace Azure.Functions.WorkerProxy.ManagedIdentity;

internal static class ManagedIdentityEndpoint
{
    internal const string Path = "/msi/token";

    private const string ApiVersionQuery = "api-version";
    private const string ClientIdQuery = "client_id";
    private const string IdentityHeaderEnvironment = "IDENTITY_HEADER";
    private const string IdentityHeaderName = "X-IDENTITY-HEADER";
    private const string MsiSecretEnvironment = "MSI_SECRET";
    private const string MsiSecretHeaderName = "Secret";
    private const string ResourceQuery = "resource";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Path, GetTokenAsync).AllowAnonymous();
    }

    private static async Task<IResult> GetTokenAsync(
        HttpRequest request,
        WorkerPodStateManager stateManager,
        FunctionRpcRelay relay,
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

        if (!TryGetRequiredQueryValue(request.Query, ResourceQuery, out string? resource)
            || !TryGetRequiredQueryValue(request.Query, ApiVersionQuery, out string? apiVersion)
            || !TryGetOptionalQueryValue(request.Query, ClientIdQuery, out string? clientId))
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_request",
                "The request must include one resource and api-version value and at most one client_id value.");
        }

        RpcTokenResponse response;
        try
        {
            response = await relay.RequestManagedIdentityTokenAsync(new RpcTokenRequest
            {
                Resource = resource,
                ClientId = clientId ?? string.Empty,
                ApiVersion = apiVersion
            }, cancellationToken);
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
            || string.IsNullOrWhiteSpace(response.AccessToken))
        {
            string detail = response.Result?.Exception?.Message
                ?? response.Result?.Result
                ?? "The runtime rejected the managed identity token request.";
            return RetryableFailure(request.HttpContext.Response, detail);
        }

        return TypedResults.Json(
            new ManagedIdentityTokenHttpResponse(
                response.AccessToken,
                response.TokenType,
                response.ExpiresOn,
                response.Resource,
                response.ClientId),
            ManagedIdentityJsonContext.Default.ManagedIdentityTokenHttpResponse);
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
