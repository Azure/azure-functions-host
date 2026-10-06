// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Logging;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Moq;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests.Logging;

public class WorkerSystemLogSinkTests
{
    [Fact]
    public async Task EmitAsync_UsesInjectedReceiptTimeAndWritesFormattedRecord()
    {
        DateTimeOffset timestamp = new(2026, 10, 6, 10, 5, 17, TimeSpan.Zero);
        Mock<TimeProvider> timeProvider = new();
        Mock<IWorkerSystemLogWriter> writer = new();
        timeProvider.Setup(instance => instance.GetUtcNow()).Returns(timestamp);
        writer.Setup(instance => instance.WriteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        WorkerSystemLogSink sink = new(timeProvider.Object, new WorkerSystemLogFormatter(), writer.Object);
        RpcLog rpcLog = new() { Level = RpcLog.Types.Level.Warning, Message = "message" };

        await sink.EmitAsync(rpcLog, CancellationToken.None);

        timeProvider.Verify(instance => instance.GetUtcNow(), Times.Once());
        writer.Verify(instance => instance.WriteAsync(
            It.Is<string>(record =>
                record.StartsWith("MS_FUNCTIONS_WORKER_POD_LOGS 3,", StringComparison.Ordinal)
                && record.Contains("2026-10-06T10:05:17.0000000Z", StringComparison.Ordinal)),
            CancellationToken.None), Times.Once());
    }
}
