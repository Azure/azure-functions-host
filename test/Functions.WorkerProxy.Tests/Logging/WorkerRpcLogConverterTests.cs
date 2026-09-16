// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Numerics;
using Azure.Functions.WorkerProxy.Logging;
using Google.Protobuf;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public class WorkerRpcLogConverterTests
{
    private readonly WorkerRpcLogConverter _converter = new();

    [Fact]
    public void Convert_UserLogPreservesProtocolFieldsAndEventId()
    {
        RpcLog rpcLog = new()
        {
            InvocationId = "invocation",
            Category = "Functions.ProcessOrder",
            LogCategory = RpcLog.Types.RpcLogCategory.User,
            Level = RpcLog.Types.Level.Warning,
            Message = "message",
            EventId = "event-name"
        };
        rpcLog.PropertiesMap.Add("ignored", new TypedData { String = "not-forwarded" });

        WorkerUserLog result = Assert.IsType<WorkerUserLog>(_converter.Convert(rpcLog));

        Assert.Equal("invocation", result.InvocationId);
        Assert.Equal(LogLevel.Warning, result.Level);
        Assert.Equal("message", result.Message);
        Assert.Equal(0, result.EventId.Id);
        Assert.Equal("event-name", result.EventId.Name);
        Assert.Null(result.Exception);
    }

    [Theory]
    [InlineData(RpcLog.Types.Level.Trace, LogLevel.Trace)]
    [InlineData(RpcLog.Types.Level.Debug, LogLevel.Debug)]
    [InlineData(RpcLog.Types.Level.Information, LogLevel.Information)]
    [InlineData(RpcLog.Types.Level.Warning, LogLevel.Warning)]
    [InlineData(RpcLog.Types.Level.Error, LogLevel.Error)]
    [InlineData(RpcLog.Types.Level.Critical, LogLevel.Critical)]
    [InlineData(RpcLog.Types.Level.None, LogLevel.None)]
    public void Convert_UserLogMapsEveryLevel(RpcLog.Types.Level level, LogLevel expected)
    {
        WorkerUserLog result = Assert.IsType<WorkerUserLog>(_converter.Convert(new RpcLog { Level = level }));

        Assert.Equal(expected, result.Level);
    }

    [Theory]
    [InlineData(RpcLog.Types.Level.Warning, LogLevel.Warning)]
    [InlineData(RpcLog.Types.Level.Information, LogLevel.Information)]
    [InlineData(RpcLog.Types.Level.Error, LogLevel.Error)]
    [InlineData(RpcLog.Types.Level.Trace, LogLevel.Information)]
    [InlineData(RpcLog.Types.Level.Debug, LogLevel.Information)]
    [InlineData(RpcLog.Types.Level.Critical, LogLevel.Information)]
    [InlineData(RpcLog.Types.Level.None, LogLevel.Information)]
    public void Convert_SystemLogMatchesHostLevelMapping(RpcLog.Types.Level level, LogLevel expected)
    {
        RpcLog rpcLog = new()
        {
            Category = "worker.system",
            LogCategory = RpcLog.Types.RpcLogCategory.System,
            Level = level,
            Message = "system message",
            EventId = "ignored-event"
        };

        WorkerSystemLog result = Assert.IsType<WorkerSystemLog>(_converter.Convert(rpcLog));

        Assert.Equal(expected, result.Level);
        Assert.Equal("system message", result.Message);
    }

    [Fact]
    public void Convert_UnknownLevelMatchesHostBehavior()
    {
        RpcLog.Types.Level unknown = (RpcLog.Types.Level)42;

        WorkerUserLog user = Assert.IsType<WorkerUserLog>(_converter.Convert(new RpcLog { Level = unknown }));
        WorkerSystemLog system = Assert.IsType<WorkerSystemLog>(_converter.Convert(new RpcLog
        {
            LogCategory = RpcLog.Types.RpcLogCategory.System,
            Level = unknown
        }));

        Assert.Equal((LogLevel)42, user.Level);
        Assert.Equal(LogLevel.Information, system.Level);
    }

    [Fact]
    public void Convert_UnknownCategoryUsesUserPath()
    {
        RpcLog.Types.RpcLogCategory unknown = (RpcLog.Types.RpcLogCategory)42;

        WorkerUserLog result = Assert.IsType<WorkerUserLog>(_converter.Convert(new RpcLog { LogCategory = unknown }));

        Assert.NotNull(result);
    }

    [Fact]
    public void Convert_UserExceptionMatchesHostObservableShape()
    {
        RpcLog rpcLog = new()
        {
            Message = "outer result",
            Exception = new RpcException
            {
                Source = "worker",
                StackTrace = "stack",
                Message = "Password=secret",
                IsUserException = true,
                Type = "WorkerException"
            }
        };

        WorkerUserLog result = Assert.IsType<WorkerUserLog>(_converter.Convert(rpcLog));

        Assert.Equal(
            new WorkerLogException("outer result", "[Hidden Credential]", "stack"),
            result.Exception);
    }

    [Fact]
    public void Convert_SystemErrorUsesHostObservableException()
    {
        WorkerSystemLog error = Assert.IsType<WorkerSystemLog>(_converter.Convert(new RpcLog
        {
            LogCategory = RpcLog.Types.RpcLogCategory.System,
            Level = RpcLog.Types.Level.Error,
            Message = "outer result",
            Exception = new RpcException
            {
                Source = "worker",
                StackTrace = "stack",
                Message = "AccountKey=secret",
                IsUserException = true,
                Type = "WorkerException"
            }
        }));

        Assert.Equal(
            new WorkerLogException("outer result", "[Hidden Credential]", "stack"),
            error.Exception);
    }

    [Theory]
    [InlineData(RpcLog.Types.Level.Warning)]
    [InlineData(RpcLog.Types.Level.Information)]
    [InlineData(RpcLog.Types.Level.Trace)]
    [InlineData(RpcLog.Types.Level.Debug)]
    [InlineData(RpcLog.Types.Level.Critical)]
    [InlineData(RpcLog.Types.Level.None)]
    public void Convert_SystemNonErrorIgnoresException(RpcLog.Types.Level level)
    {
        WorkerSystemLog result = Assert.IsType<WorkerSystemLog>(_converter.Convert(new RpcLog
        {
            LogCategory = RpcLog.Types.RpcLogCategory.System,
            Level = level,
            Exception = new RpcException { Message = "failure" }
        }));

        Assert.Null(result.Exception);
    }

    [Fact]
    public void Convert_EmptyFieldsRemainEmpty()
    {
        WorkerUserLog result = Assert.IsType<WorkerUserLog>(_converter.Convert(new RpcLog()));

        Assert.Equal(string.Empty, result.InvocationId);
        Assert.Equal(string.Empty, result.Message);
        Assert.Equal(string.Empty, result.EventId.Name);
    }

    [Fact]
    public void Convert_CustomMetricExtractsNameValueAndConvertsRemainingProperties()
    {
        RpcLog rpcLog = CreateMetric();
        rpcLog.PropertiesMap.Add("String", new TypedData { String = "value" });
        rpcLog.PropertiesMap.Add("Json", new TypedData { Json = "{\"enabled\":true}" });
        rpcLog.PropertiesMap.Add("Bytes", new TypedData { Bytes = ByteString.CopyFromUtf8("bytes") });
        rpcLog.PropertiesMap.Add("Int", new TypedData { Int = 42 });
        rpcLog.PropertiesMap.Add("Double", new TypedData { Double = 2.5 });
        rpcLog.PropertiesMap.Add("Null", new TypedData());

        WorkerCustomMetric result = Assert.IsType<WorkerCustomMetric>(_converter.Convert(rpcLog));

        Assert.Equal("invocation", result.InvocationId);
        Assert.Equal("OrdersProcessed", result.Name);
        Assert.Equal(1.5, result.Value);
        Assert.Equal("value", result.Properties["String"]);
        Dictionary<string, object?> json = Assert.IsType<Dictionary<string, object?>>(result.Properties["Json"]);
        Assert.Equal(true, json["enabled"]);
        Assert.Equal("bytes", ByteString.CopyFrom(Assert.IsType<byte[]>(result.Properties["Bytes"])).ToStringUtf8());
        Assert.Equal(42L, result.Properties["Int"]);
        Assert.Equal(2.5, result.Properties["Double"]);
        Assert.Null(result.Properties["Null"]);
        Assert.False(result.Properties.ContainsKey("Name"));
        Assert.False(result.Properties.ContainsKey("Value"));
    }

    [Fact]
    public void Convert_JsonObjectAndNestedValuesUseClrCollections()
    {
        WorkerCustomMetric result = ConvertMetricJson(
            "{\"name\":\"sample\",\"items\":[1,2.5,true,null,{\"date\":\"2026-09-16T08:20:31Z\"}]}");

        Dictionary<string, object?> root = Assert.IsType<Dictionary<string, object?>>(result.Properties["Json"]);
        Assert.Equal("sample", root["name"]);
        List<object?> items = Assert.IsType<List<object?>>(root["items"]);
        Assert.Equal(1L, items[0]);
        Assert.Equal(2.5, items[1]);
        Assert.Equal(true, items[2]);
        Assert.Null(items[3]);
        Dictionary<string, object?> nested = Assert.IsType<Dictionary<string, object?>>(items[4]);
        Assert.Equal("2026-09-16T08:20:31Z", nested["date"]);
        Assert.IsType<string>(nested["date"]);
    }

    [Fact]
    public void Convert_JsonArrayUsesClrList()
    {
        WorkerCustomMetric result = ConvertMetricJson("[1,\"two\",false,null]");

        List<object?> array = Assert.IsType<List<object?>>(result.Properties["Json"]);
        Assert.Equal(new object?[] { 1L, "two", false, null }, array);
    }

    [Theory]
    [InlineData("\"text\"", "text", typeof(string))]
    [InlineData("42", 42L, typeof(long))]
    [InlineData("2.5", 2.5, typeof(double))]
    [InlineData("true", true, typeof(bool))]
    [InlineData("\"2026-09-16T08:20:31Z\"", "2026-09-16T08:20:31Z", typeof(string))]
    public void Convert_JsonScalarUsesClrPrimitive(string json, object expected, Type expectedType)
    {
        WorkerCustomMetric result = ConvertMetricJson(json);

        object value = result.Properties["Json"]!;
        Assert.NotNull(value);
        Assert.IsType(expectedType, value);
        Assert.Equal(expected, value);
    }

    [Fact]
    public void Convert_JsonNullReturnsNull()
    {
        WorkerCustomMetric result = ConvertMetricJson("null");

        Assert.Null(result.Properties["Json"]);
    }

    [Fact]
    public void Convert_JsonIntegerLargerThanInt64UsesBigInteger()
    {
        const string value = "9223372036854775808";

        WorkerCustomMetric result = ConvertMetricJson(value);

        Assert.Equal(BigInteger.Parse(value), Assert.IsType<BigInteger>(result.Properties["Json"]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("undefined")]
    [InlineData(" undefined ")]
    public void Convert_HostNullJsonValuesReturnNull(string json)
    {
        WorkerCustomMetric result = ConvertMetricJson(json);

        Assert.Null(result.Properties["Json"]);
    }

    [Fact]
    public void Convert_JsonObjectDuplicateKeysKeepLastValue()
    {
        WorkerCustomMetric result = ConvertMetricJson("{\"value\":1,\"value\":2}");

        Dictionary<string, object?> value = Assert.IsType<Dictionary<string, object?>>(result.Properties["Json"]);
        Assert.Equal(2L, value["value"]);
    }

    [Theory]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 1)]
    public void Convert_IncompleteCustomMetricReturnsExplicitFailure(
        bool includeName,
        bool includeValue,
        int expected)
    {
        RpcLog rpcLog = new() { InvocationId = "invocation", LogCategory = RpcLog.Types.RpcLogCategory.CustomMetric };
        if (includeName)
        {
            rpcLog.PropertiesMap.Add("Name", new TypedData { String = "metric" });
        }

        if (includeValue)
        {
            rpcLog.PropertiesMap.Add("Value", new TypedData { Double = 1 });
        }

        WorkerRpcLogDropped result =
            Assert.IsType<WorkerRpcLogDropped>(_converter.Convert(rpcLog));

        Assert.Equal("invocation", result.InvocationId);
        Assert.Equal((WorkerRpcLogDropReason)expected, result.Reason);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Convert_CustomMetricWrongRequiredTypeUsesProtobufDefault(bool wrongNameType)
    {
        RpcLog rpcLog = CreateMetric();
        if (wrongNameType)
        {
            rpcLog.PropertiesMap["Name"] = new TypedData { Int = 1 };
        }
        else
        {
            rpcLog.PropertiesMap["Value"] = new TypedData { String = "1" };
        }

        WorkerCustomMetric result = Assert.IsType<WorkerCustomMetric>(_converter.Convert(rpcLog));

        Assert.Equal(wrongNameType ? string.Empty : "OrdersProcessed", result.Name);
        Assert.Equal(wrongNameType ? 1.5 : 0, result.Value);
        Assert.False(result.Properties.ContainsKey("Name"));
        Assert.False(result.Properties.ContainsKey("Value"));
    }

    [Fact]
    public void Convert_DoesNotMutateOriginalCustomMetric()
    {
        RpcLog rpcLog = CreateMetric();
        rpcLog.PropertiesMap.Add("Region", new TypedData { String = "westus" });
        RpcLog original = rpcLog.Clone();

        _ = _converter.Convert(rpcLog);

        Assert.Equal(original, rpcLog);
    }

    [Fact]
    public void Convert_UnsupportedMetricPropertyMatchesHostFailure()
    {
        RpcLog rpcLog = CreateMetric();
        rpcLog.PropertiesMap.Add("Collection", new TypedData
        {
            CollectionString = new CollectionString { String = { "value" } }
        });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => _converter.Convert(rpcLog));

        Assert.Contains(nameof(TypedData.DataOneofCase.CollectionString), exception.Message);
    }

    private static RpcLog CreateMetric()
    {
        RpcLog rpcLog = new()
        {
            InvocationId = "invocation",
            LogCategory = RpcLog.Types.RpcLogCategory.CustomMetric
        };
        rpcLog.PropertiesMap.Add("Name", new TypedData { String = "OrdersProcessed" });
        rpcLog.PropertiesMap.Add("Value", new TypedData { Double = 1.5 });
        return rpcLog;
    }

    private WorkerCustomMetric ConvertMetricJson(string json)
    {
        RpcLog rpcLog = CreateMetric();
        rpcLog.PropertiesMap.Add("Json", new TypedData { Json = json });

        return Assert.IsType<WorkerCustomMetric>(_converter.Convert(rpcLog));
    }
}