// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text.Json.Nodes;
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
        Assert.Equal("Functions.ProcessOrder", result.LoggerCategory);
        Assert.Equal(RpcLog.Types.RpcLogCategory.User, result.RpcLogCategory);
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

        Assert.Equal("worker.system", result.LoggerCategory);
        Assert.Equal(RpcLog.Types.RpcLogCategory.System, result.RpcLogCategory);
        Assert.Equal(expected, result.Level);
        Assert.Equal("system message", result.Message);
        Assert.Equal(0, result.EventId.Id);
        Assert.Null(result.EventId.Name);
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

        Assert.Equal(unknown, result.RpcLogCategory);
    }

    [Fact]
    public void Convert_UserExceptionPreservesEveryProtocolField()
    {
        RpcLog rpcLog = new()
        {
            Exception = new RpcException
            {
                Source = "worker",
                StackTrace = "stack",
                Message = "exception message",
                IsUserException = true,
                Type = "WorkerException"
            }
        };

        WorkerUserLog result = Assert.IsType<WorkerUserLog>(_converter.Convert(rpcLog));

        Assert.Equal(new WorkerLogException("worker", "stack", "exception message", true, "WorkerException"), result.Exception);
    }

    [Fact]
    public void Convert_SystemExceptionIsUsedOnlyForError()
    {
        RpcException exception = new() { Message = "failure" };

        WorkerSystemLog error = Assert.IsType<WorkerSystemLog>(_converter.Convert(new RpcLog
        {
            LogCategory = RpcLog.Types.RpcLogCategory.System,
            Level = RpcLog.Types.Level.Error,
            Exception = exception
        }));
        WorkerSystemLog warning = Assert.IsType<WorkerSystemLog>(_converter.Convert(new RpcLog
        {
            LogCategory = RpcLog.Types.RpcLogCategory.System,
            Level = RpcLog.Types.Level.Warning,
            Exception = exception
        }));

        Assert.NotNull(error.Exception);
        Assert.Null(warning.Exception);
    }

    [Fact]
    public void Convert_EmptyFieldsRemainEmpty()
    {
        WorkerUserLog result = Assert.IsType<WorkerUserLog>(_converter.Convert(new RpcLog()));

        Assert.Equal(string.Empty, result.InvocationId);
        Assert.Equal(string.Empty, result.LoggerCategory);
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
        Assert.Equal("{\"enabled\":true}", Assert.IsType<JsonObject>(result.Properties["Json"]).ToJsonString());
        Assert.Equal("bytes", ByteString.CopyFrom(Assert.IsType<byte[]>(result.Properties["Bytes"])).ToStringUtf8());
        Assert.Equal(42L, result.Properties["Int"]);
        Assert.Equal(2.5, result.Properties["Double"]);
        Assert.Null(result.Properties["Null"]);
        Assert.False(result.Properties.ContainsKey("Name"));
        Assert.False(result.Properties.ContainsKey("Value"));
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

        WorkerCustomMetricConversionFailure result =
            Assert.IsType<WorkerCustomMetricConversionFailure>(_converter.Convert(rpcLog));

        Assert.Equal("invocation", result.InvocationId);
        Assert.Equal((WorkerCustomMetricConversionFailureReason)expected, result.Reason);
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    public void Convert_InvalidCustomMetricTypeReturnsExplicitFailure(
        bool invalidName,
        int expected)
    {
        RpcLog rpcLog = CreateMetric();
        if (invalidName)
        {
            rpcLog.PropertiesMap["Name"] = new TypedData { Int = 1 };
        }
        else
        {
            rpcLog.PropertiesMap["Value"] = new TypedData { String = "1" };
        }

        WorkerCustomMetricConversionFailure result =
            Assert.IsType<WorkerCustomMetricConversionFailure>(_converter.Convert(rpcLog));

        Assert.Equal((WorkerCustomMetricConversionFailureReason)expected, result.Reason);
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
}