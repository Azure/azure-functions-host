// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Azure.Functions.WorkerProxy.Logging;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Moq;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests.Logging;

public class WorkerSystemLogSinkTests
{
    [Fact]
    public void TryEmit_UsesInjectedReceiptTimeAndEnqueuesFormattedRecord()
    {
        DateTimeOffset timestamp = new(2026, 10, 6, 10, 5, 17, TimeSpan.Zero);
        Mock<TimeProvider> timeProvider = new();
        Mock<IWorkerSystemLogWriter> writer = new();
        timeProvider.Setup(instance => instance.GetUtcNow()).Returns(timestamp);
        writer.Setup(instance => instance.TryEnqueue(It.IsAny<string>()))
            .Returns(WorkerSystemLogEnqueueResult.Accepted);
        WorkerSystemLogSink sink = new(timeProvider.Object, new WorkerSystemLogFormatter(), writer.Object);
        RpcLog rpcLog = new() { Level = RpcLog.Types.Level.Warning, Message = "message" };

        WorkerSystemLogEnqueueResult result = sink.TryEmit(rpcLog);

        Assert.Equal(WorkerSystemLogEnqueueResult.Accepted, result);
        timeProvider.Verify(instance => instance.GetUtcNow(), Times.Once());
        writer.Verify(instance => instance.TryEnqueue(
            It.Is<string>(record =>
                record.StartsWith("MS_FUNCTIONS_WORKER_POD_LOGS 3,", StringComparison.Ordinal)
                && record.Contains("2026-10-06T10:05:17.0000000Z", StringComparison.Ordinal))), Times.Once());
    }

    [Theory]
    [InlineData((int)WorkerSystemLogEnqueueResult.QueueFull)]
    [InlineData((int)WorkerSystemLogEnqueueResult.PipelineFaulted)]
    [InlineData((int)WorkerSystemLogEnqueueResult.PipelineStopping)]
    public void TryEmit_ReturnsWriterResult(int expectedValue)
    {
        WorkerSystemLogEnqueueResult expected = (WorkerSystemLogEnqueueResult)expectedValue;
        Mock<IWorkerSystemLogWriter> writer = new();
        writer.Setup(instance => instance.TryEnqueue(It.IsAny<string>())).Returns(expected);
        WorkerSystemLogSink sink = new(TimeProvider.System, new WorkerSystemLogFormatter(), writer.Object);

        WorkerSystemLogEnqueueResult result = sink.TryEmit(new RpcLog());

        Assert.Equal(expected, result);
    }
}
