// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Logging;
using Azure.Functions.WorkerProxy.State;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests.Diagnostics;

public partial class WorkerSystemLoggingTests
{
    [Theory]
    [InlineData(LogLevel.Information, "4")]
    [InlineData(LogLevel.Warning, "3")]
    [InlineData(LogLevel.Error, "2")]
    [InlineData(LogLevel.Critical, "1")]
    public async Task Metadata_PreservesWireColumnsSeverityAndReadableSummary(LogLevel level, string wireLevel)
    {
        StringWriter output = new();
        using WorkerPodEventGenerator generator = new(output, TimeProvider.System);
        WorkerSystemLogContext context = WorkerSystemLogContext.FromState(
            WorkerPodState.CreateUnassigned("test-pod", Timestamp) with
            {
                Revision = 3,
                AssignmentState = WorkerAssignmentState.Ready,
                FunctionAppName = "test-app",
                FunctionGroupName = "http",
                WorkerId = "worker",
                SessionId = 7,
                IsWorkerAttached = true
            }) with
        {
            RequestId = "request",
            Operation = "Http",
            Phase = "Forwarding",
            Outcome = "TransportFailure",
            ReasonCode = "ForwarderError",
            StatusCode = 202,
            ResponseStarted = true,
            ElapsedMs = 12.5,
            BuildVersion = "1.2.3+revision"
        };
        generator.LogEvent(level, "Failure", "Proxy", "detail,\"text\"\r\nnext",
            "Human-readable, \"summary\".", string.Empty, string.Empty, context, Timestamp);
        await generator.CompleteAsync();

        string[] fields = Assert.Single(ParseCsvOutput(output.ToString()));
        Assert.Equal(21, fields.Length);
        Assert.Equal(wireLevel, fields[0]);
        Assert.Equal("detail,'text' next", fields[6]);
        Assert.StartsWith("WorkerId=worker; ProxySessionId=7; WP1[", fields[7], StringComparison.Ordinal);
        Assert.Contains("p=test-pod;a=test-app;g=http;r=3;", fields[7], StringComparison.Ordinal);
        Assert.Contains("ms=12.500;", fields[7], StringComparison.Ordinal);
        Assert.Contains("b=1.2.3+revision;", fields[7], StringComparison.Ordinal);
        Assert.EndsWith("]; Human-readable, 'summary'.", fields[7], StringComparison.Ordinal);
        Assert.All(new[] { 1, 2, 3, 8, 12, 13, 14, 15, 16, 17, 18, 19, 20 }, index => Assert.Empty(fields[index]));
    }

    [Fact]
    public async Task Metadata_BoundsCompleteEnvelopeAndReportsOmissions()
    {
        StringWriter output = new();
        using WorkerPodEventGenerator generator = new(output, TimeProvider.System);
        string maximum = new('x', 128);
        WorkerSystemLogContext context = WorkerSystemLogContext.FromState(
            WorkerPodState.CreateUnassigned(maximum, Timestamp) with
            {
                FunctionAppName = maximum,
                FunctionGroupName = maximum,
                WorkerId = maximum,
                SessionId = long.MaxValue
            }) with
        {
            RequestId = maximum,
            InvocationId = maximum,
            FunctionId = maximum,
            Operation = "Http",
            Phase = "Forwarding",
            Outcome = "TransportFailure",
            ReasonCode = "ForwarderError",
            BuildVersion = maximum
        };
        generator.LogEvent(LogLevel.Warning, "Failure", "Proxy", string.Empty,
            "Readable diagnostic. " + new string('x', 2_000), string.Empty, string.Empty, context);
        await generator.CompleteAsync();

        string summary = Assert.Single(ParseCsvOutput(output.ToString()))[7];
        Assert.Equal(1_024, summary.Length);
        Assert.Contains("omit=1;]; Readable diagnostic.", summary, StringComparison.Ordinal);
        Assert.Matches(@"WP1\[(?:[a-z]+=[A-Za-z0-9._:+-]{1,128};)+\]; ", summary);
        Assert.True(summary.IndexOf("]; ", StringComparison.Ordinal) <= 896);
    }

    [Theory]
    [InlineData("app,quoted\";\r\nvalue")]
    [InlineData("unicode-\u2603-\uD83D\uDE80")]
    public async Task Metadata_FingerprintsUnsafeIdentityWithoutChangingColumns(string identity)
    {
        StringWriter output = new();
        using WorkerPodEventGenerator generator = new(output, TimeProvider.System);
        WorkerSystemLogContext context = WorkerSystemLogContext.FromState(
            WorkerPodState.CreateUnassigned("pod", Timestamp) with { FunctionAppName = identity });
        generator.LogEvent(LogLevel.Information, "Identity", "Proxy", string.Empty, "Accepted.",
            string.Empty, string.Empty, context);
        await generator.CompleteAsync();

        string[] fields = Assert.Single(ParseCsvOutput(output.ToString()));
        Assert.Equal(21, fields.Length);
        Assert.Contains("a=sha256:", fields[7], StringComparison.Ordinal);
        Assert.DoesNotContain(identity, fields[7], StringComparison.Ordinal);
        Assert.NotEmpty(new UTF8Encoding(false, true).GetBytes(output.ToString()));
    }

    [Fact]
    public void Metadata_NewSnapshotDoesNotInheritAnOlderSessionsIdentity()
    {
        WorkerSystemLogContext outer = WorkerSystemLogContext.FromState(
            WorkerPodState.CreateUnassigned("pod", Timestamp) with { FunctionAppName = "old-app", WorkerId = "old-worker", SessionId = 1 });
        WorkerSystemLogContext inner = WorkerSystemLogContext.FromState(WorkerPodState.CreateUnassigned("pod", Timestamp));
        Assert.Same(inner, outer.Merge(inner));
        Assert.Null(outer.Merge(inner).WorkerId);
        Assert.Null(outer.Merge(inner).State!.FunctionAppName);
    }

    [Fact]
    public async Task Metadata_ConcurrentScopesRetainIndependentSnapshots()
    {
        CapturingEventGenerator generator = new();
        using ILoggerFactory factory = CreateLoggerFactory(generator);
        ILogger logger = factory.CreateLogger(WorkerProxyLogExporter.ApplicationCategory);
        await Task.WhenAll(Enumerable.Range(0, 30).Select(async index =>
        {
            string identity = index.ToString(CultureInfo.InvariantCulture);
            WorkerSystemLogContext context = WorkerSystemLogContext.FromState(
                WorkerPodState.CreateUnassigned("pod", Timestamp) with { FunctionAppName = identity, Revision = index });
            using IDisposable? scope = logger.BeginScope(context);
            await Task.Yield();
            logger.LogInformation("{Identity}", identity);
        }));

        Assert.Equal(30, generator.ProxyRecords.Count);
        Assert.All(generator.ProxyRecords, record =>
        {
            Assert.Equal(record.Message, record.Context!.State!.FunctionAppName);
            Assert.Equal(record.Message, record.Context.State.Revision.ToString(CultureInfo.InvariantCulture));
        });
    }

    [Fact]
    public async Task ConsoleLogging_FullBufferDoesNotBlockProducerAndReportsLoss()
    {
        GatedLogWriter output = new();
        using WorkerPodEventGenerator generator = new(output, TimeProvider.System);
        try
        {
            generator.LogEvent(LogLevel.Information, "First", "Proxy", string.Empty, "First", string.Empty, string.Empty);
            await output.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Run(() =>
            {
                for (int index = 0; index < 8_100; index++)
                {
                    generator.LogEvent(LogLevel.Information, "Buffered", "Proxy", string.Empty, "Buffered", string.Empty, string.Empty);
                }
            }).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(generator.DroppedRecords >= 100);
        }
        finally
        {
            output.Release.TrySetResult();
            await generator.CompleteAsync();
        }
    }

    [Fact]
    public async Task ConsoleLogging_FailedWriterDoesNotThrowIntoProducerOrRetry()
    {
        FailingLogWriter output = new();
        using WorkerPodEventGenerator generator = new(output, TimeProvider.System);
        generator.LogEvent(LogLevel.Information, "First", "Proxy", string.Empty, "First", string.Empty, string.Empty);
        await generator.CompleteAsync();
        generator.LogEvent(LogLevel.Information, "Later", "Proxy", string.Empty, "Later", string.Empty, string.Empty);
        Assert.Equal(1, output.Attempts);
        Assert.Equal(1, generator.DroppedRecords);
    }

    private sealed class GatedLogWriter : StringWriter
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task WriteLineAsync(string? value)
        {
            Started.TrySetResult();
            await Release.Task;
        }

        public override Task WriteAsync(string? value) => WriteLineAsync(value);
    }

    private sealed class FailingLogWriter : StringWriter
    {
        public int Attempts { get; private set; }

        public override Task WriteLineAsync(string? value)
        {
            Attempts++;
            return Task.FromException(new IOException("synthetic-writer-failure"));
        }

        public override Task WriteAsync(string? value) => WriteLineAsync(value);
    }
}
