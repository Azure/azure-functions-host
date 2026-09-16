// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json;
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
            (LogLevel)rpcLog.Level,
            rpcLog.Message,
            new EventId(0, rpcLog.EventId),
            ConvertException(rpcLog.Message, rpcLog.Exception));
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
            level,
            rpcLog.Message,
            level == LogLevel.Error ? ConvertException(rpcLog.Message, rpcLog.Exception) : null);
    }

    private static WorkerRpcLogConversionResult ConvertCustomMetric(RpcLog rpcLog)
    {
        if (!rpcLog.PropertiesMap.TryGetValue(MetricNameKey, out TypedData? metricName))
        {
            return new WorkerRpcLogDropped(
                rpcLog.InvocationId, WorkerRpcLogDropReason.MissingMetricName);
        }

        if (!rpcLog.PropertiesMap.TryGetValue(MetricValueKey, out TypedData? metricValue))
        {
            return new WorkerRpcLogDropped(
                rpcLog.InvocationId, WorkerRpcLogDropReason.MissingMetricValue);
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
            TypedData.DataOneofCase.Json => ConvertJson(typedData.Json),
            TypedData.DataOneofCase.Bytes or TypedData.DataOneofCase.Stream => typedData.Bytes.ToByteArray(),
            TypedData.DataOneofCase.Http => ConvertHttp(typedData.Http),
            TypedData.DataOneofCase.Int => typedData.Int,
            TypedData.DataOneofCase.Double => typedData.Double,
            _ => throw new InvalidOperationException($"Unknown RpcDataType: {typedData.DataCase}")
        };
    }

    // JSON metric properties use Native AOT-safe CLR primitives and collections. Their values are
    // semantically equivalent to the host values without preserving Newtonsoft JObject/JArray types.
    private static object? ConvertJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)
            || string.Equals(json.Trim(), "undefined", StringComparison.Ordinal))
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(json);

        return ConvertJsonElement(document.RootElement);
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => ConvertJsonObject(element),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(ConvertJsonElement)
                .ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out long value) => value,
            JsonValueKind.Number when BigInteger.TryParse(
                element.GetRawText(), NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger value) => value,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => throw new InvalidOperationException($"Unsupported JSON value kind: {element.ValueKind}")
        };
    }

    private static Dictionary<string, object?> ConvertJsonObject(JsonElement element)
    {
        Dictionary<string, object?> result = new(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            result[property.Name] = ConvertJsonElement(property.Value);
        }

        return result;
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

    private static WorkerLogException? ConvertException(string result, RpcException? exception)
    {
        return exception is null
            ? null
            : new WorkerLogException(
                result,
                WorkerLogSanitizer.Sanitize(exception.Message),
                exception.StackTrace);
    }
}