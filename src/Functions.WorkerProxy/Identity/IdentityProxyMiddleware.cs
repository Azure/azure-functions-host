// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Management;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Azure.WebJobs.Script.Grpc.HttpProxy;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Azure.Functions.WorkerProxy.Identity;

internal sealed class IdentityProxyMiddleware(IOptions<IdentityProxyOptions> options, IdentityProxyService proxy,
    TimeProvider timeProvider, ILogger<IdentityProxyMiddleware> logger, IConfiguration configuration)
{
    private readonly IdentityProxyOptions _options = options.Value;
    private readonly byte[] _secretHash = HashInjectedSecret(configuration);

    internal async Task InvokeAsync(HttpContext context)
    {
        long started = timeProvider.GetTimestamp();
        using CancellationTokenSource deadline = new(_options.RequestTimeout, timeProvider);
        using CancellationTokenSource operation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, context.RequestAborted);
        try
        {
            if (!context.Request.Headers.TryGetValue(HttpProxyProtocol.IdentityHeader, out StringValues header)
                || header is not [string secret] || string.IsNullOrEmpty(secret)
                || Encoding.UTF8.GetByteCount(secret) > _options.MaxHeaderBytes
                || !CryptographicOperations.FixedTimeEquals(_secretHash, SHA256.HashData(Encoding.UTF8.GetBytes(secret))))
            {
                await ErrorAsync(context, StatusCodes.Status403Forbidden, "InvalidIdentityHeader");
                return;
            }

            string target = context.Features.Get<IHttpRequestFeature>()?.RawTarget
                ?? string.Concat(context.Request.PathBase, context.Request.Path, context.Request.QueryString);
            string path = target.Split('?', 2)[0];
            if (!string.Equals(path, _options.TokenPath, StringComparison.OrdinalIgnoreCase))
            {
                await ErrorAsync(context, StatusCodes.Status404NotFound, "InvalidTokenPath");
                return;
            }

            HttpProxyLimits limits = _options.ToLimits();
            HttpProxyProtocol.ValidateTarget(target, _options.TokenPath, limits);
            if (!HttpProxyProtocol.IsToken(context.Request.Method)
                || string.Equals(context.Request.Method, "CONNECT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(context.Request.Method, "TRACE", StringComparison.OrdinalIgnoreCase)
                || context.Request.Headers.ContainsKey("Upgrade"))
            {
                throw new HttpProxyException(HttpProxyFailure.Types.Code.InvalidRequest);
            }

            using HttpProxyAdmission.Lease? admission = proxy.Admission.TryAcquire(_options.ReservationBytes);
            if (admission is null)
            {
                throw new HttpProxyException(HttpProxyFailure.Types.Code.TooManyRequests);
            }

            HttpProxyRequest request = new() { Method = context.Request.Method, Target = target };
            request.Headers.Add(HttpProxyProtocol.FilterHeaders(
                context.Request.Headers.Select(static entry => HttpProxyProtocol.Header(entry.Key, entry.Value.Select(static value => value ?? string.Empty))),
                limits));
            if (context.Request.ContentLength > _options.MaxRequestBodyBytes)
            {
                throw new HttpProxyException(HttpProxyFailure.Types.Code.RequestTooLarge);
            }

            request.Body = await HttpProxyProtocol.ReadBodyAsync(context.Request.Body, _options.MaxRequestBodyBytes,
                response: false, operation.Token);
            HttpProxyMessage terminal = await proxy.SendAsync(request, admission, started, operation.Token);
            if (terminal.Failure is { } failure)
            {
                throw new HttpProxyException(failure.Code);
            }

            HttpProxyResponse response = terminal.Response;
            context.Response.StatusCode = (int)response.StatusCode;
            foreach (HttpProxyHeader responseHeader in response.Headers)
            {
                context.Response.Headers[responseHeader.Name] = new StringValues(responseHeader.Values.ToArray());
            }

            if (!HttpMethods.IsHead(request.Method) && response.StatusCode is not (204 or 304))
            {
                context.Response.ContentLength = response.Body.Length;
                await context.Response.Body.WriteAsync(response.Body.Memory, operation.Token);
            }
            else if (response.HasContentLength && response.StatusCode != 204)
            {
                context.Response.ContentLength = response.ContentLength;
            }
        }
        catch (HttpProxyException exception)
        {
            int status = exception.Code switch
            {
                HttpProxyFailure.Types.Code.InvalidRequest => 400,
                HttpProxyFailure.Types.Code.RequestTooLarge => 413,
                HttpProxyFailure.Types.Code.HeadersTooLarge => 431,
                HttpProxyFailure.Types.Code.TooManyRequests => 429,
                HttpProxyFailure.Types.Code.DeadlineExceeded => 504,
                HttpProxyFailure.Types.Code.Cancelled => 504,
                HttpProxyFailure.Types.Code.ResponseTooLarge or HttpProxyFailure.Types.Code.BadGateway => 502,
                _ => 503
            };
            await ErrorAsync(context, status, exception.Code.ToString());
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug("Identity proxy HTTP caller disconnected.");
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            await ErrorAsync(context, StatusCodes.Status504GatewayTimeout, "DeadlineExceeded");
        }
        catch (OperationCanceledException)
        {
            await ErrorAsync(context, StatusCodes.Status503ServiceUnavailable, "TransportUnavailable");
        }
    }

    private static byte[] HashInjectedSecret(IConfiguration configuration)
    {
        string? secret = configuration[IdentityProxyOptions.SecretSettingName];
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        return SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    private async Task ErrorAsync(HttpContext context, int status, string code)
    {
        logger.LogWarning("Identity proxy request failed: {Reason}.", code);
        if (context.Response.HasStarted)
        {
            context.Abort();
            return;
        }

        context.Response.Headers.Clear();
        context.Response.Headers.CacheControl = "no-store";
        if (status == StatusCodes.Status429TooManyRequests)
        {
            context.Response.Headers.RetryAfter = "1";
        }

        await TypedResults.Json(new WorkerApiErrorResponse(new(code, "The identity proxy request could not be completed.")),
            WorkerProxyJsonContext.Default.WorkerApiErrorResponse, statusCode: status).ExecuteAsync(context);
    }
}
