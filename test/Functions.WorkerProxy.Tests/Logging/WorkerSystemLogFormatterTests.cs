// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Azure.Functions.WorkerProxy.Logging;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests.Logging;

public class WorkerSystemLogFormatterTests
{
    private readonly WorkerSystemLogFormatter _formatter = new();

    [Fact]
    public void Format_ProducesFunctionsLogsCompatibleRecord()
    {
        RpcLog rpcLog = new()
        {
            Level = RpcLog.Types.Level.Error,
            EventId = "Worker,Failure",
            Category = "UntrustedCategory",
            Message = "Worker \"failed\"\r\nunexpectedly",
            InvocationId = "invocation,id",
            Exception = new RpcException
            {
                Type = "System.InvalidOperationException",
                Message = "Invalid \"operation\", retry",
                StackTrace = "line one\r\nline two"
            }
        };
        DateTimeOffset timestamp = new(2026, 10, 6, 10, 5, 17, TimeSpan.Zero);

        string actual = _formatter.Format(rpcLog, timestamp);

        Assert.Equal(
            "MS_FUNCTIONS_WORKER_POD_LOGS " +
            "2,,,,Worker Failure,Worker.LanguageWorker,\"line one  line two\",\"Worker 'failed'  unexpectedly\",," +
            "2026-10-06T10:05:17.0000000Z,System.InvalidOperationException,\"Invalid 'operation', retry\"," +
            "invocation id,,,,,,,,",
            actual);
        Assert.DoesNotContain("UntrustedCategory", actual, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RpcLog.Types.Level.Trace, "5")]
    [InlineData(RpcLog.Types.Level.Debug, "5")]
    [InlineData(RpcLog.Types.Level.Information, "4")]
    [InlineData(RpcLog.Types.Level.Warning, "3")]
    [InlineData(RpcLog.Types.Level.Error, "2")]
    [InlineData(RpcLog.Types.Level.Critical, "1")]
    [InlineData(RpcLog.Types.Level.None, "0")]
    public void Format_MapsSeverityToFunctionsEventLevel(RpcLog.Types.Level level, string expected)
    {
        RpcLog rpcLog = new() { Level = level };

        string actual = _formatter.Format(rpcLog, DateTimeOffset.UnixEpoch);

        Assert.StartsWith($"{WorkerSystemLogFormatter.Prefix}{expected},", actual, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_TruncatesDetailsAndKeepsSinglePhysicalLine()
    {
        RpcLog rpcLog = new()
        {
            Message = "summary\nnext",
            Exception = new RpcException
            {
                StackTrace = new string('x', WorkerSystemLogFormatter.MaximumDetailsLength + 1) + "\r\nextra"
            }
        };

        string actual = _formatter.Format(rpcLog, DateTimeOffset.UnixEpoch);

        Assert.DoesNotContain('\r', actual);
        Assert.DoesNotContain('\n', actual);
        Assert.Contains($"\"{new string('x', WorkerSystemLogFormatter.MaximumDetailsLength)}\"", actual, StringComparison.Ordinal);
        Assert.DoesNotContain("extra", actual, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_MissingOptionalValuesProducesEmptyFields()
    {
        RpcLog rpcLog = new() { Level = RpcLog.Types.Level.Information, Message = "message" };

        string actual = _formatter.Format(rpcLog, DateTimeOffset.UnixEpoch);

        Assert.Equal(
            "MS_FUNCTIONS_WORKER_POD_LOGS 4,,,,,Worker.LanguageWorker,\"\",\"message\",," +
            "1970-01-01T00:00:00.0000000Z,,\"\",,,,,,,,,",
            actual);
    }
}
