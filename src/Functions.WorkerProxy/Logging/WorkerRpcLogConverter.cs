// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text.Json.Nodes;
using Google.Protobuf;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.WorkerProxy.Logging;

internal sealed class WorkerRpcLogConverter : IWorkerRpcLogConverter
{
    private const string MetricNameKey = "Name";
    private const string MetricValueKey = "Value";
    private static readonly IReadOnlyDictionary<string, object> EmptyHeaders = new Dictionary<string, object>();

    public WorkerRpcLogConversionResult Convert(RpcLog rpcLog)
    {
        ArgumentNullException.ThrowIfNull(rpcLog);

        return rpcLog.LogCategory switch
        {
            RpcLog.Types.RpcLogCategory.System => ConvertSystemLog(rpcLog),
            RpcLog.Types.RpcLogCategory.CustomMetric => ConvertCustomMetric(rpcLog),
            _ => ConvertUserLog(rpcLog)
        };
    }

    private static WorkerUserLog ConvertUserLog(RpcLog rpcLog)
    {
        return new WorkerUserLog(
            rpcLog.InvocationId,
            rpcLog.Category,
            rpcLog.LogCategory,
            (LogLevel)rpcLog.Level,
            rpcLog.Message,
            new EventId(0, rpcLog.EventId),
            ConvertException(rpcLog.Exception));
    }

    private static WorkerSystemLog ConvertSystemLog(RpcLog rpcLog)
    {
        LogLevel level = (LogLevel)rpcLog.Level switch
        {
            LogLevel.Warning => LogLevel.Warning,
            LogLevel.Information => LogLevel.Information,
            LogLevel.Error => LogLevel.Error,
            _ => LogLevel.Information
        };

        return new WorkerSystemLog(
            rpcLog.Category,
            rpcLog.LogCategory,
            level,
            rpcLog.Message,
            new EventId(0),
            level == LogLevel.Error ? ConvertException(rpcLog.Exception) : null);
    }

    private static WorkerRpcLogConversionResult ConvertCustomMetric(RpcLog rpcLog)
    {
        if (!rpcLog.PropertiesMap.TryGetValue(MetricNameKey, out TypedData? metricName))
        {
            return new WorkerCustomMetricConversionFailure(
                rpcLog.InvocationId, WorkerCustomMetricConversionFailureReason.MissingName);
        }

        if (!rpcLog.PropertiesMap.TryGetValue(MetricValueKey, out TypedData? metricValue))
        {
            return new WorkerCustomMetricConversionFailure(
                rpcLog.InvocationId, WorkerCustomMetricConversionFailureReason.MissingValue);
        }

        if (metricName.DataCase != TypedData.DataOneofCase.String)
        {
            return new WorkerCustomMetricConversionFailure(
                rpcLog.InvocationId, WorkerCustomMetricConversionFailureReason.InvalidNameType);
        }

        if (metricValue.DataCase != TypedData.DataOneofCase.Double)
        {
            return new WorkerCustomMetricConversionFailure(
                rpcLog.InvocationId, WorkerCustomMetricConversionFailureReason.InvalidValueType);
        }

        Dictionary<string, object?> properties = rpcLog.PropertiesMap
            .Where(static property => !string.Equals(property.Key, MetricNameKey, StringComparison.Ordinal)
                && !string.Equals(property.Key, MetricValueKey, StringComparison.Ordinal))
            .ToDictionary(static property => property.Key, static property => ConvertProperty(property.Value), StringComparer.Ordinal);

        return new WorkerCustomMetric(rpcLog.InvocationId, metricName.String, metricValue.Double, properties);
    }

    private static object? ConvertProperty(TypedData typedData)
    {
        return typedData.DataCase switch
        {
            TypedData.DataOneofCase.None => null,
            TypedData.DataOneofCase.String => typedData.String,
            TypedData.DataOneofCase.Json => JsonNode.Parse(typedData.Json),
            TypedData.DataOneofCase.Bytes or TypedData.DataOneofCase.Stream => typedData.Bytes.ToByteArray(),
            TypedData.DataOneofCase.Http => ConvertHttp(typedData.Http),
            TypedData.DataOneofCase.Int => typedData.Int,
            TypedData.DataOneofCase.Double => typedData.Double,
            _ => throw new InvalidOperationException($"Unknown RpcDataType: {typedData.DataCase}")
        };
    }

    private static ExpandoObject? ConvertHttp(RpcHttp? input)
    {
        if (input is null)
        {
            return null;
        }

        ExpandoObject expando = new();
        IDictionary<string, object?> values = expando;
        values["method"] = input.Method;
        values["query"] = input.Query;
        values["statusCode"] = input.StatusCode;
        values["enableContentNegotiation"] = input.EnableContentNegotiation;

        if (input.Headers.Count > 0)
        {
            values["headers"] = input.Headers.ToDictionary(static header => header.Key, static header => (object)header.Value);
        }
        else
        {
            values["headers"] = EmptyHeaders;
        }

        if (input.Cookies.Count > 0)
        {
            values["cookies"] = input.Cookies.Select(ConvertCookie).ToList();
        }
        else
        {
            values["cookies"] = Array.Empty<Tuple<string, string, CookieOptions>>();
        }

        if (input.Body is not null)
        {
            values["body"] = ConvertProperty(input.Body);
        }

        return expando;
    }

    private static Tuple<string, string, CookieOptions> ConvertCookie(RpcHttpCookie cookie)
    {
        CookieOptions options = new();
        if (cookie.Domain is not null)
        {
            options.Domain = cookie.Domain.Value;
        }

        if (cookie.Path is not null)
        {
            options.Path = cookie.Path.Value;
        }

        if (cookie.Secure is not null)
        {
            options.Secure = cookie.Secure.Value;
        }

        options.SameSite = cookie.SameSite switch
        {
            RpcHttpCookie.Types.SameSite.Strict => SameSiteMode.Strict,
            RpcHttpCookie.Types.SameSite.Lax => SameSiteMode.Lax,
            RpcHttpCookie.Types.SameSite.ExplicitNone => SameSiteMode.None,
            _ => SameSiteMode.Unspecified
        };

        if (cookie.HttpOnly is not null)
        {
            options.HttpOnly = cookie.HttpOnly.Value;
        }

        if (cookie.Expires is not null)
        {
            options.Expires = cookie.Expires.Value.ToDateTimeOffset();
        }

        if (cookie.MaxAge is not null)
        {
            options.MaxAge = TimeSpan.FromSeconds(cookie.MaxAge.Value);
        }

        return Tuple.Create(cookie.Name, cookie.Value, options);
    }

    private static WorkerLogException? ConvertException(RpcException? exception)
    {
        return exception is null
            ? null
            : new WorkerLogException(
                exception.Source,
                exception.StackTrace,
                exception.Message,
                exception.IsUserException,
                exception.Type);
    }
}