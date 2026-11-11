// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Metrics;

public sealed class FlexConsumptionWorkerMetricsTests : IDisposable
{
    private const string MetricsPath = @"C:\metrics";
    private readonly MockFileSystem _fileSystem = new();
    private readonly Mock<IHostMetricsProvider> _provider = new();
    private readonly TestLogger<FlexConsumptionPerWorkerMetricsPublisher> _logger = new();
    private readonly FlexConsumptionPerWorkerMetricsPublisher _publisher;
    private readonly FlexConsumptionMetricsPublisherOptions _options;
    private readonly HashSet<string> _stampedFiles = new(StringComparer.Ordinal);
    private DateTime _now = new(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc);

    public FlexConsumptionWorkerMetricsTests()
    {
        Mock<TimeProvider> time = new();
        time.Setup(provider => provider.GetUtcNow()).Returns(() => new DateTimeOffset(_now));
        Mock<IOptionsMonitor<StandbyOptions>> standby = new();
        standby.SetupGet(options => options.CurrentValue).Returns(new StandbyOptions());
        _provider.SetupGet(provider => provider.InstanceId).Returns("runtime-pod");
        _provider.SetupGet(provider => provider.FunctionGroup).Returns("http");
        _options = new()
        {
            UseWorkerAttribution = true,
            MetricsFilePath = MetricsPath,
            MaxFileCount = 2,
            InitialPublishDelayMS = (int)TimeSpan.FromDays(1).TotalMilliseconds,
            MetricsPublishIntervalMS = (int)TimeSpan.FromDays(1).TotalMilliseconds
        };
        _publisher = new(new TestEnvironment(), standby.Object, Options.Create(_options), _logger,
            _fileSystem, _provider.Object, time.Object);

        // MockFileSystem assigns every file the same creation time, so provide advancing times.
        DateTime creationTime = _now;
        _publisher.MetricsFileManager.GetCreationTimeUtc = _ => creationTime = creationTime.AddTicks(1);
    }

    [Fact]
    public async Task NonSeparatedFlex_PreservesRuntimeIdentityAndCounters()
    {
        using var publisher = CreateTraditionalPublisher();
        publisher.OnFunctionStarted("function", "invocation", _now);
        _now = _now.AddSeconds(1);
        publisher.OnFunctionCompleted("function", "invocation", _now);
        await publisher.OnPublishMetrics(_now, ValueStopwatch.StartNew());

        var metrics = Assert.Single(ReadMetrics());
        Assert.Equal("runtime-pod", metrics.InstanceId);
        Assert.Equal("http", metrics.FunctionGroup);
        Assert.Equal(1, metrics.ExecutionCount);
        Assert.Equal(1000, metrics.ExecutionTimeMS);
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "")]
    [InlineData(false, "runtime-group")]
    [InlineData(true, "runtime-group")]
    public async Task OnPublishMetrics_UsesEachWorkersAssignmentInsteadOfRuntimeDimensions(bool runtimeAlwaysReady, string runtimeGroup)
    {
        _publisher.IsAlwaysReady = runtimeAlwaysReady;
        _provider.SetupGet(provider => provider.FunctionGroup).Returns(runtimeGroup);
        _publisher.WorkerLinked("http-ar", "http", true);
        _publisher.WorkerLinked("http-od", "http", false);
        _publisher.WorkerLinked("queue-ar", "function:QueueProcessor", true);
        _publisher.WorkerLinked("queue-od", "function:QueueProcessor", false);
        foreach (string workerId in new[] { "http-ar", "http-od", "queue-ar", "queue-od" })
        {
            Dispatch(workerId, workerId);
        }

        _now = _now.AddSeconds(1);
        foreach (string workerId in new[] { "http-ar", "http-od", "queue-ar", "queue-od" })
        {
            _publisher.InvocationCompleted(workerId, success: true);
        }

        await PublishAsync();

        var metrics = ReadMetrics().ToDictionary(record => record.InstanceId);
        Assert.Equal(4, metrics.Count);
        Assert.Equal("http", metrics["http-ar"].FunctionGroup);
        Assert.Equal("http", metrics["http-od"].FunctionGroup);
        Assert.Equal("function:QueueProcessor", metrics["queue-ar"].FunctionGroup);
        Assert.Equal("function:QueueProcessor", metrics["queue-od"].FunctionGroup);
        Assert.True(metrics["http-ar"].IsAlwaysReady);
        Assert.True(metrics["queue-ar"].IsAlwaysReady);
        Assert.False(metrics["http-od"].IsAlwaysReady);
        Assert.False(metrics["queue-od"].IsAlwaysReady);
        Assert.All(metrics.Values, record =>
        {
            Assert.Equal(1, record.ExecutionCount);
            Assert.Equal(1, record.ExecutionSuccessCount);
            Assert.Equal(1, record.StartedInvocationCount);
            Assert.Equal(1000, record.ExecutionTimeMS);
            Assert.Equal(1000, record.TotalTimeMS);
        });
        _provider.VerifyGet(provider => provider.FunctionGroup, Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AlwaysReady_MixedWorkerModesPublishOnlyAssignedIdleBaseline(bool runtimeAlwaysReady)
    {
        _publisher.IsAlwaysReady = runtimeAlwaysReady;
        _publisher.WorkerLinked("ar", "function:QueueProcessor", true);
        _publisher.WorkerLinked("od", "http", false);
        _now = _now.AddSeconds(1);
        await PublishAsync();

        var baseline = Assert.Single(ReadMetrics());
        Assert.Equal("ar", baseline.InstanceId);
        Assert.Equal("function:QueueProcessor", baseline.FunctionGroup);
        Assert.True(baseline.IsAlwaysReady);
        Assert.Equal(1000, baseline.TotalTimeMS);
        Assert.Equal(0, baseline.ExecutionTimeMS);
        Assert.Equal(0, baseline.ExecutionCount);

        _publisher.WorkerUnlinked("ar");
        _now = _now.AddMilliseconds(_options.KeepAliveIntervalMS);
        await PublishAsync();
        _now = _now.AddSeconds(1);
        await PublishAsync();

        var metrics = ReadMetrics();
        Assert.Equal(2, metrics.Count(record => string.Equals(record.InstanceId, "ar", StringComparison.Ordinal)));
        var keepAlive = Assert.Single(metrics.Where(record => string.Equals(record.InstanceId, "od", StringComparison.Ordinal)));
        Assert.Equal("http", keepAlive.FunctionGroup);
        Assert.False(keepAlive.IsAlwaysReady);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkerLinked_RepeatedLinkPreservesAssignmentAndInterval(bool initialAlwaysReady)
    {
        _publisher.WorkerLinked("worker", "function:QueueProcessor", initialAlwaysReady);
        Dispatch("invocation", "worker");
        _now = _now.AddSeconds(1);
        _publisher.WorkerLinked("worker", "http", !initialAlwaysReady);
        _now = _now.AddSeconds(1);
        _publisher.InvocationCompleted("invocation", success: true);
        await PublishAsync();

        var metrics = Assert.Single(ReadMetrics());
        Assert.Equal("function:QueueProcessor", metrics.FunctionGroup);
        Assert.Equal(initialAlwaysReady, metrics.IsAlwaysReady);
        Assert.Equal(2000, metrics.TotalTimeMS);
        Assert.Equal(2000, metrics.ExecutionTimeMS);
        Assert.Equal(1, metrics.ExecutionCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingBillingMode_TracksInvocationsWithoutRuntimeFallbackAndWarnsOnce(bool runtimeAlwaysReady)
    {
        _publisher.IsAlwaysReady = runtimeAlwaysReady;
        _publisher.WorkerLinked("worker", "function:QueueProcessor", null);
        _publisher.WorkerLinked("worker", "http", true);
        Dispatch("invocation", "worker");
        _now = _now.AddSeconds(1);
        await PublishAsync();
        _publisher.InvocationCompleted("invocation", success: true);
        _now = _now.AddMilliseconds(_options.KeepAliveIntervalMS);
        await PublishAsync();
        Dispatch("next", "worker");
        _publisher.WorkerUnlinked("worker");
        await PublishAsync();

        Assert.Empty(ReadMetrics());
        var warning = Assert.Single(_logger.GetLogMessages().Where(message => message.Level == LogLevel.Warning));
        Assert.Contains("worker", warning.FormattedMessage, StringComparison.Ordinal);
        Assert.Contains("function:QueueProcessor", warning.FormattedMessage, StringComparison.Ordinal);
        Assert.Contains("No Runtime billing fallback", warning.FormattedMessage, StringComparison.Ordinal);
        Assert.False(_publisher.TryDispatchInvocation("late", "worker", () => true));
        _provider.VerifyGet(provider => provider.FunctionGroup, Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void WorkerLinked_MissingGroupRejectsAttribution(string functionGroup)
    {
        Assert.ThrowsAny<ArgumentException>(() => _publisher.WorkerLinked("worker", functionGroup, false));
        Assert.False(_publisher.TryDispatchInvocation("invocation", "worker", () => true));
    }

    [Fact]
    public async Task Dispatch_WaitingForWorkerDoesNotMeterRuntimePreparation()
    {
        _publisher.WorkerLinked("worker", "http", false);
        _publisher.OnFunctionStarted("function", "invocation", _now);
        _now = _now.AddSeconds(10);
        await PublishAsync();
        Assert.Empty(ReadMetrics());
        Dispatch("invocation", "worker");
        _now = _now.AddSeconds(1);
        _publisher.InvocationCompleted("invocation", success: false);
        await PublishAsync();

        Assert.Equal(1000, Assert.Single(ReadMetrics()).ExecutionTimeMS);
    }

    [Fact]
    public async Task KeepAlive_PublishesEachLinkedIdleWorker()
    {
        _publisher.WorkerLinked("first", "http", false);
        _publisher.WorkerLinked("second", "http", false);
        _now = _now.AddMilliseconds(_options.KeepAliveIntervalMS);
        await PublishAsync();

        var metrics = ReadMetrics();
        Assert.Equal(2, metrics.Count);
        Assert.All(metrics, record =>
        {
            Assert.Equal(0, record.ExecutionCount);
            Assert.Equal(0, record.ExecutionTimeMS);
            Assert.False(record.IsAlwaysReady);
        });
    }

    [Fact]
    public async Task DispatchAccepted_TracksInvocationUntilCompletion()
    {
        await PublishAsync();
        _publisher.WorkerLinked("worker", "http", false);
        Mock<Func<bool>> dispatch = new();
        dispatch.Setup(post => post()).Returns(true);

        Assert.True(_publisher.TryDispatchInvocation("invocation", "worker", dispatch.Object));
        dispatch.Verify(post => post(), Times.Once);

        _now = _now.AddSeconds(1);
        _publisher.InvocationCompleted("invocation", success: false);
        await PublishAsync();

        FlexConsumptionMetricsPublisherBase.Metrics metric = Assert.Single(ReadMetrics());
        Assert.Equal(1, metric.ExecutionCount);
        Assert.Equal(0, metric.ExecutionSuccessCount);
    }

    [Fact]
    public async Task Completion_CountsOnceDespiteLateCancellationAndDuplicateCompletion()
    {
        using CancellationTokenSource cancellation = new();
        _publisher.WorkerLinked("worker", "http", false);
        Dispatch("invocation", "worker");
        _now = _now.AddSeconds(1);
        _publisher.InvocationCompleted("invocation", success: false);
        _now = _now.AddSeconds(1);
        cancellation.Cancel();
        _publisher.InvocationCompleted("invocation", success: false);
        await PublishAsync();

        var metrics = Assert.Single(ReadMetrics());
        Assert.Equal(1, metrics.ExecutionCount);
        Assert.Equal(1000, metrics.ExecutionTimeMS);
        Assert.Equal(0, metrics.ActiveInvocationCount);
    }

    [Fact]
    public async Task OnPublishMetrics_FiveInvocationsUseWorkerIdentity()
    {
        const string workerId = "0--dad293e8-bbb0-46f5-a7a7-75f331e3a55b";
        _publisher.WorkerLinked(workerId, "http", false);
        for (int i = 0; i < 5; i++)
        {
            string invocationId = i.ToString();
            _publisher.OnFunctionStarted("function", invocationId, _now);
            Dispatch(invocationId, workerId);
            _now = _now.AddSeconds(1);
            _publisher.InvocationCompleted(invocationId, success: false);
        }

        await PublishAsync();

        var metrics = Assert.Single(ReadMetrics());
        Assert.Equal(workerId, metrics.InstanceId);
        Assert.Equal(5, metrics.ExecutionCount);
        Assert.Equal(5000, metrics.ExecutionTimeMS);
        Assert.Equal(5, metrics.StartedInvocationCount);
        Assert.Equal(0, metrics.ActiveInvocationCount);
        Assert.Equal("http", metrics.FunctionGroup);
    }

    [Fact]
    public async Task OnPublishMetrics_TracksSuccessPerWorkerAndResetsBetweenPublications()
    {
        _publisher.WorkerLinked("worker-a", "http", false);
        _publisher.WorkerLinked("worker-b", "http", false);
        Dispatch("success", "worker-a");
        Dispatch("failure", "worker-b");
        _now = _now.AddSeconds(1);
        _publisher.InvocationCompleted("success", success: true);
        _publisher.InvocationCompleted("failure", success: false);
        _publisher.InvocationCompleted("success", success: true);
        await PublishAsync();

        var metrics = ReadMetrics().ToDictionary(record => record.InstanceId);
        Assert.Equal(1, metrics["worker-a"].ExecutionCount);
        Assert.Equal(1, metrics["worker-a"].ExecutionSuccessCount);
        Assert.Equal(1, metrics["worker-b"].ExecutionCount);
        Assert.Equal(0, metrics["worker-b"].ExecutionSuccessCount);

        Dispatch("next", "worker-a");
        _now = _now.AddSeconds(1);
        _publisher.InvocationCompleted("next", success: false);
        await PublishAsync();

        var workerMetrics = ReadMetrics().Where(record => string.Equals(record.InstanceId, "worker-a", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, workerMetrics.Length);
        Assert.Equal(2, workerMetrics.Sum(record => record.ExecutionCount));
        Assert.Equal(1, workerMetrics.Sum(record => record.ExecutionSuccessCount));
    }

    [Fact]
    public async Task OnPublishMetrics_DeferredSnapshotPreservesActivityAndUnlinkedWorker()
    {
        _publisher.MetricsFileManager.GetCreationTimeUtc = _ => _now;
        _publisher.MetricsFileManager.DelayAsync = _ => Task.CompletedTask;
        _publisher.MetricsFileManager.MaxWorkerFileAttempts = 2;
        _publisher.WorkerLinked("worker-a", "http", false);
        _publisher.WorkerLinked("worker-b", "http", false);
        Dispatch("a1", "worker-a");
        Dispatch("b1", "worker-b");
        _now = _now.AddSeconds(1);
        _publisher.InvocationCompleted("a1", success: true);
        _publisher.InvocationCompleted("b1", success: true);

        await PublishAsync();
        Assert.Equal("worker-a", Assert.Single(ReadMetrics()).InstanceId);

        Dispatch("b2", "worker-b");
        _now = _now.AddSeconds(1);
        _publisher.InvocationCompleted("b2", success: false);
        _publisher.WorkerUnlinked("worker-b");
        DateTime creationTime = _now;
        _publisher.MetricsFileManager.GetCreationTimeUtc = _ => creationTime = creationTime.AddTicks(1);

        await PublishAsync();
        await PublishAsync();

        var records = ReadMetrics();
        var workerBRecords = records.Where(record => string.Equals(record.InstanceId, "worker-b", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, records.Count);
        Assert.Equal(2, workerBRecords.Length);
        Assert.Equal(3, records.Sum(record => record.ExecutionCount));
        Assert.Equal(2, records.Sum(record => record.ExecutionSuccessCount));
        Assert.Equal(3, records.Sum(record => record.StartedInvocationCount));
        Assert.Equal(3000, records.Sum(record => record.ExecutionTimeMS));
        Assert.Equal(2000, workerBRecords.Sum(record => record.TotalTimeMS));
        Assert.False(_publisher.TryDispatchInvocation("late", "worker-b", () => true));
    }

    [Fact]
    public async Task OnPublishMetrics_WorkerIdReusedWhileDeferredPreservesBothAssignments()
    {
        _publisher.MetricsFileManager.GetCreationTimeUtc = _ => _now;
        _publisher.MetricsFileManager.MaxWorkerFileAttempts = 1;
        _publisher.WorkerLinked("first", "http", false);
        _publisher.WorkerLinked("shared", "http", false);
        Dispatch("first", "first");
        Dispatch("old", "shared");
        _now = _now.AddSeconds(1);
        _publisher.InvocationCompleted("first", success: true);
        _publisher.InvocationCompleted("old", success: true);
        await PublishAsync();
        Assert.Single(ReadMetrics());

        _publisher.WorkerUnlinked("shared");
        _publisher.WorkerLinked("shared", "function:New", true);
        Dispatch("new", "shared");
        _now = _now.AddSeconds(1);
        _publisher.InvocationCompleted("new", success: true);
        DateTime creationTime = _now;
        _publisher.MetricsFileManager.GetCreationTimeUtc = _ => creationTime = creationTime.AddTicks(1);
        await PublishAsync();

        var records = ReadMetrics().Where(record => string.Equals(record.InstanceId, "shared", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, records.Length);
        Assert.Contains(records, record => string.Equals(record.FunctionGroup, "http", StringComparison.Ordinal) &&
            !record.IsAlwaysReady && record.ExecutionCount == 1);
        Assert.Contains(records, record => string.Equals(record.FunctionGroup, "function:New", StringComparison.Ordinal) &&
            record.IsAlwaysReady && record.ExecutionCount == 1);
        Assert.Equal(2000, records.Sum(record => record.ExecutionTimeMS));
        Assert.Equal(2000, records.Sum(record => record.TotalTimeMS));
    }

    [Fact]
    public async Task OnPublishMetrics_WriteFailureRetainsSnapshotUntilRetry()
    {
        _publisher.MetricsFileManager.GetCreationTimeUtc = _ => throw new IOException("Creation time unavailable.");
        _publisher.WorkerLinked("worker", "http", false);
        Dispatch("invocation", "worker");
        _now = _now.AddSeconds(1);
        _publisher.InvocationCompleted("invocation", success: true);

        await PublishAsync();
        Assert.Empty(ReadMetrics());
        Assert.Contains(_logger.GetLogMessages(), message =>
            message.Level == LogLevel.Error && message.Exception is IOException);

        _publisher.MetricsFileManager.GetCreationTimeUtc = _ => _now;
        await PublishAsync();
        await PublishAsync();

        var record = Assert.Single(ReadMetrics());
        Assert.Equal(1, record.ExecutionCount);
        Assert.Equal(1, record.ExecutionSuccessCount);
        Assert.Equal(1, record.StartedInvocationCount);
        Assert.Equal(1000, record.ExecutionTimeMS);
    }

    [Fact]
    public async Task OnPublishMetrics_HostMetricsFailureRetainsCapturedSnapshot()
    {
        _provider.SetupSequence(provider => provider.GetHostMetricsOrNull())
            .Throws(new InvalidOperationException("Host metrics unavailable."))
            .Returns((IReadOnlyDictionary<string, long>)null);
        _publisher.WorkerLinked("worker", "http", false);
        Dispatch("invocation", "worker");
        _now = _now.AddSeconds(1);
        _publisher.InvocationCompleted("invocation", success: true);

        await PublishAsync();
        Assert.Empty(ReadMetrics());
        await PublishAsync();

        var record = Assert.Single(ReadMetrics());
        Assert.Equal(1, record.ExecutionCount);
        Assert.Equal(1, record.ExecutionSuccessCount);
        Assert.Equal(1000, record.ExecutionTimeMS);
        Assert.Contains(_logger.GetLogMessages(), message =>
            message.Level == LogLevel.Error && message.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task OnPublishMetrics_RefreshesCaptureTimeAfterDrainingPendingSnapshots()
    {
        _publisher.MetricsFileManager.GetCreationTimeUtc = _ => _now;
        _publisher.MetricsFileManager.DelayAsync = _ => Task.CompletedTask;
        _publisher.MetricsFileManager.MaxWorkerFileAttempts = 2;
        _publisher.WorkerLinked("worker-a", "http", false);
        _publisher.WorkerLinked("worker-b", "http", false);
        Dispatch("a1", "worker-a");
        Dispatch("b1", "worker-b");
        _now = _now.AddSeconds(1);
        _publisher.InvocationCompleted("a1", success: true);
        _publisher.InvocationCompleted("b1", success: true);
        await PublishAsync();

        TaskCompletionSource delayStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _publisher.MetricsFileManager.DelayAsync = _ =>
        {
            delayStarted.TrySetResult();
            return resume.Task;
        };
        Task publishing = _publisher.OnPublishMetrics(_now, ValueStopwatch.StartNew());
        try
        {
            await delayStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Dispatch("a2", "worker-a");
            _now = _now.AddSeconds(1);
            _publisher.InvocationCompleted("a2", success: true);
            DateTime creationTime = _now;
            _publisher.MetricsFileManager.GetCreationTimeUtc = _ => creationTime = creationTime.AddTicks(1);
        }
        finally
        {
            resume.TrySetResult();
        }

        await publishing;
        var workerARecords = ReadMetrics().Where(record => string.Equals(record.InstanceId, "worker-a", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, workerARecords.Length);
        Assert.Equal(2, workerARecords.Sum(record => record.ExecutionCount));
        Assert.Equal(2000, workerARecords.Sum(record => record.ExecutionTimeMS));
        Assert.Equal(2000, workerARecords.Sum(record => record.TotalTimeMS));
    }

    [Fact]
    public async Task OnPublishMetrics_RefreshesCaptureTimeAfterWaitingForAnotherPublication()
    {
        _options.WorkerMetricsFileRetentionPeriod = TimeSpan.FromDays(365 * 30);
        _publisher.MetricsFileManager.GetCreationTimeUtc = _ => _now;
        _publisher.MetricsFileManager.MaxWorkerFileAttempts = 2;
        TaskCompletionSource delayStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _publisher.MetricsFileManager.DelayAsync = _ =>
        {
            delayStarted.TrySetResult();
            return resume.Task;
        };
        _publisher.WorkerLinked("worker-a", "http", false);
        _publisher.WorkerLinked("worker-b", "http", false);
        Dispatch("a1", "worker-a");
        Dispatch("b1", "worker-b");
        _now = _now.AddSeconds(1);
        _publisher.InvocationCompleted("a1", success: true);
        _publisher.InvocationCompleted("b1", success: true);

        Task first = _publisher.OnPublishMetrics(_now, ValueStopwatch.StartNew());
        Task second = Task.CompletedTask;
        try
        {
            await delayStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            second = _publisher.OnPublishMetrics(_now, ValueStopwatch.StartNew());
            Dispatch("a2", "worker-a");
            _now = _now.AddSeconds(1);
            _publisher.InvocationCompleted("a2", success: true);
            DateTime creationTime = _now;
            _publisher.MetricsFileManager.GetCreationTimeUtc = _ => creationTime = creationTime.AddTicks(1);
        }
        finally
        {
            resume.TrySetResult();
        }

        await Task.WhenAll(first, second);
        var workerARecords = ReadMetrics().Where(record => string.Equals(record.InstanceId, "worker-a", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, workerARecords.Length);
        Assert.Equal(2, workerARecords.Sum(record => record.ExecutionCount));
        Assert.Equal(2000, workerARecords.Sum(record => record.ExecutionTimeMS));
        Assert.Equal(2000, workerARecords.Sum(record => record.TotalTimeMS));
    }

    [Fact]
    public async Task WorkerUnlinked_DoesNotCountLateSuccess()
    {
        using CancellationTokenSource cancellation = new();
        _publisher.WorkerLinked("worker", "http", false);
        Dispatch("invocation", "worker");
        _now = _now.AddSeconds(1);
        _publisher.WorkerUnlinked("worker");

        _publisher.InvocationCompleted("invocation", success: true);
        await PublishAsync();

        var metrics = Assert.Single(ReadMetrics());
        Assert.Equal(1, metrics.ExecutionCount);
        Assert.Equal(0, metrics.ExecutionSuccessCount);
    }

    [Fact]
    public async Task OnPublishMetrics_ConcurrentWorkersProduceIndependentFiles()
    {
        _publisher.WorkerLinked("worker-a", "http", false);
        _publisher.WorkerLinked("worker-b", "http", false);
        Dispatch("a1", "worker-a");
        Dispatch("b1", "worker-b");
        _now = _now.AddSeconds(1);
        _publisher.InvocationCompleted("a1", success: false);
        _now = _now.AddSeconds(1);
        _publisher.InvocationCompleted("b1", success: false);

        await PublishAsync();

        var metrics = ReadMetrics().ToDictionary(record => record.InstanceId);
        Assert.Equal(2, metrics.Count);
        Assert.Equal(1000, metrics["worker-a"].ExecutionTimeMS);
        Assert.Equal(2000, metrics["worker-b"].ExecutionTimeMS);
        Assert.All(metrics.Values, record => Assert.Equal(1, record.ExecutionCount));
        Assert.DoesNotContain("runtime-pod", metrics.Keys);
    }

    [Fact]
    public async Task OnPublishMetrics_OverlappingInvocationsMeterWorkerTimeOnce()
    {
        _publisher.WorkerLinked("worker", "http", false);
        Dispatch("first", "worker");
        _now = _now.AddMilliseconds(500);
        Dispatch("second", "worker");
        _now = _now.AddMilliseconds(500);
        _publisher.InvocationCompleted("first", success: false);
        _now = _now.AddSeconds(1);
        _publisher.InvocationCompleted("second", success: false);

        await PublishAsync();

        var metrics = Assert.Single(ReadMetrics());
        Assert.Equal(2, metrics.ExecutionCount);
        Assert.Equal(2000, metrics.ExecutionTimeMS);
    }

    [Fact]
    public async Task OnPublishMetrics_ActiveWorkerPublishesEvenInsidePreviouslyRoundedTime()
    {
        _publisher.WorkerLinked("worker", "http", false);
        Dispatch("invocation", "worker");
        _now = _now.AddMilliseconds(200);
        await PublishAsync();
        _now = _now.AddMilliseconds(200);
        await PublishAsync();

        var metrics = ReadMetrics();
        Assert.Equal(2, metrics.Count);
        Assert.All(metrics, record => Assert.Equal(1, record.ActiveInvocationCount));
        Assert.Equal(1000, metrics.Sum(record => record.ExecutionTimeMS));
        Assert.Equal(1, metrics.Sum(record => record.StartedInvocationCount));
    }

    [Fact]
    public async Task OnPublishMetrics_PreservesRoundingAcrossPublicationBoundaries()
    {
        _publisher.WorkerLinked("worker", "http", false);
        Dispatch("first", "worker");
        _now = _now.AddMilliseconds(200);
        await PublishAsync();
        _now = _now.AddMilliseconds(200);
        _publisher.InvocationCompleted("first", success: false);
        Dispatch("second", "worker");
        _now = _now.AddMilliseconds(200);
        _publisher.InvocationCompleted("second", success: false);
        await PublishAsync();

        var metrics = ReadMetrics();
        Assert.Equal(2, metrics.Count);
        Assert.Equal(1000, metrics.Sum(record => record.ExecutionTimeMS));
        Assert.Equal(2, metrics.Sum(record => record.ExecutionCount));
        Assert.Equal(1, metrics.Sum(record => record.ActiveInvocationCount));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DispatchFailure_DoesNotLeaveAttributionOrBilling(bool throws)
    {
        _publisher.WorkerLinked("worker", "http", false);
        if (throws)
        {
            Assert.Throws<InvalidOperationException>(() =>
                _publisher.TryDispatchInvocation("invocation", "worker", () => throw new InvalidOperationException()));
        }
        else
        {
            Assert.False(_publisher.TryDispatchInvocation("invocation", "worker", () => false));
        }

        await PublishAsync();
        Assert.Empty(ReadMetrics());

        Dispatch("invocation", "worker");
        _now = _now.AddSeconds(1);
        _publisher.InvocationCompleted("invocation", success: false);
        await PublishAsync();
        Assert.Equal(1, Assert.Single(ReadMetrics()).ExecutionCount);
    }

    [Fact]
    public async Task Cancellation_ContinuesMeteringUntilActualCompletion()
    {
        using CancellationTokenSource cancellation = new();
        _publisher.WorkerLinked("worker", "http", false);
        Dispatch("invocation", "worker");
        _now = _now.AddSeconds(1);
        cancellation.Cancel();
        _publisher.OnFunctionCompleted("function", "invocation", _now);
        await PublishAsync();
        var active = Assert.Single(ReadMetrics());
        Assert.Equal(0, active.ExecutionCount);
        Assert.Equal(1, active.ActiveInvocationCount);
        Assert.Equal(1000, active.ExecutionTimeMS);
        _now = _now.AddSeconds(2);
        await PublishAsync();
        Assert.Equal(3000, ReadMetrics().Sum(record => record.ExecutionTimeMS));
        Assert.All(ReadMetrics(), record => Assert.Equal(0, record.ExecutionCount));
        _now = _now.AddSeconds(2);
        _publisher.InvocationCompleted("invocation", success: true);
        await PublishAsync();

        Assert.Equal(5000, active.ExecutionTimeMS + ReadMetrics().Sum(record => record.ExecutionTimeMS));
        Assert.Equal(1, ReadMetrics().Sum(record => record.ExecutionCount));
        Assert.Equal(1, ReadMetrics().Sum(record => record.ExecutionSuccessCount));
    }

    [Fact]
    public async Task CancellationDuringDispatch_KeepsAcceptedInvocationActiveUntilCompletion()
    {
        using CancellationTokenSource cancellation = new();
        _publisher.WorkerLinked("worker", "http", false);

        Assert.True(_publisher.TryDispatchInvocation("invocation", "worker", () =>
        {
            _now = _now.AddSeconds(1);
            cancellation.Cancel();

            return true;
        }));
        await PublishAsync();
        Assert.Equal(1, Assert.Single(ReadMetrics()).ActiveInvocationCount);
        Assert.Equal(0, Assert.Single(ReadMetrics()).ExecutionCount);
        _now = _now.AddSeconds(2);
        _publisher.InvocationCompleted("invocation", success: false);
        await PublishAsync();

        Assert.Equal(1, ReadMetrics().Sum(record => record.ExecutionCount));
        Assert.Equal(3000, ReadMetrics().Sum(record => record.ExecutionTimeMS));
    }

    [Fact]
    public async Task WorkerUnlinked_FinalizesOutstandingActivityAndStopsKeepAlives()
    {
        _publisher.WorkerLinked("worker", "http", false);
        Dispatch("invocation", "worker");
        _now = _now.AddSeconds(1);
        _publisher.WorkerUnlinked("worker");
        _now = _now.AddSeconds(10);
        _publisher.InvocationCompleted("invocation", success: false);
        await PublishAsync();
        _now = _now.AddMinutes(1);
        await PublishAsync();

        var metrics = Assert.Single(ReadMetrics());
        Assert.Equal(1000, metrics.TotalTimeMS);
        Assert.Equal(1000, metrics.ExecutionTimeMS);
        Assert.Equal(1, metrics.ExecutionCount);
        Assert.False(_publisher.TryDispatchInvocation("late", "worker", () => true));
    }

    [Fact]
    public async Task AlwaysReady_PublishesIdleWorkersOnlyForTheirLinkedLifetime()
    {
        _publisher.WorkerLinked("first", "http", true);
        _now = _now.AddSeconds(1);
        _publisher.WorkerLinked("second", "http", true);
        _now = _now.AddSeconds(1);
        _publisher.WorkerUnlinked("first");
        _now = _now.AddSeconds(1);
        await PublishAsync();
        _now = _now.AddSeconds(1);
        await PublishAsync();

        var metrics = ReadMetrics();
        var first = Assert.Single(metrics.Where(record => string.Equals(record.InstanceId, "first", StringComparison.Ordinal)));
        Assert.Equal(2000, first.TotalTimeMS);
        Assert.Equal(3000, metrics.Where(record => string.Equals(record.InstanceId, "second", StringComparison.Ordinal)).Sum(record => record.TotalTimeMS));
        Assert.All(metrics, record =>
        {
            Assert.True(record.IsAlwaysReady);
            Assert.Equal(0, record.ExecutionCount);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingAttribution_DoesNotFallBackToRuntime(bool isAlwaysReady)
    {
        _publisher.IsAlwaysReady = isAlwaysReady;
        _publisher.OnFunctionStarted("function", "unattributed", _now);
        _now = _now.AddSeconds(1);
        _publisher.InvocationCompleted("unattributed", success: false);
        await PublishAsync();

        Assert.Empty(ReadMetrics());
        Assert.Contains(_logger.GetLogMessages(), message =>
            message.Level == LogLevel.Warning &&
            message.FormattedMessage.Contains("No Runtime billing fallback", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnPublishMetrics_DoesNotCopyRuntimeScaleCounters()
    {
        _provider.Setup(provider => provider.GetHostMetricsOrNull()).Returns(new Dictionary<string, long>
        {
            [Microsoft.Azure.WebJobs.Script.Metrics.HostMetrics.StartedInvocationCount] = 50,
            [Microsoft.Azure.WebJobs.Script.Metrics.HostMetrics.ActiveInvocationCount] = 25,
            [Microsoft.Azure.WebJobs.Script.Metrics.HostMetrics.AppFailureCount] = 3
        });
        _publisher.WorkerLinked("worker", "http", false);
        Dispatch("invocation", "worker");
        _now = _now.AddSeconds(1);
        await PublishAsync();

        var metrics = Assert.Single(ReadMetrics());
        Assert.Equal(1, metrics.StartedInvocationCount);
        Assert.Equal(1, metrics.ActiveInvocationCount);
        Assert.Equal(0, metrics.AppFailureCount);
    }

    [Fact]
    public void FileRetention_DefaultMatchesFifteenMinuteBuffer()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), _options.WorkerMetricsFileRetentionPeriod);
    }

    [Fact]
    public async Task FileRetention_IsIndependentPerWorker()
    {
        _publisher.WorkerLinked("worker-a", "http", true);
        _publisher.WorkerLinked("worker-b", "http", true);
        _now = _now.AddSeconds(1);
        await PublishAsync();
        _publisher.WorkerUnlinked("worker-b");
        _now = _now.AddSeconds(1);
        await PublishAsync();
        _now = _now.AddSeconds(1);
        await PublishAsync();

        var metrics = ReadMetrics().GroupBy(record => record.InstanceId).ToArray();
        Assert.Equal(2, metrics.Length);
        Assert.All(metrics, records => Assert.Equal(2, records.Count()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FileRetention_ReclaimsRetiredWorkersAndPreservesFreshFinalRecords(bool hasLinkedWorker)
    {
        for (int i = 0; i < 100; i++)
        {
            string workerId = $"retired-{i}";
            _publisher.WorkerLinked(workerId, "http", true);
            _now = _now.AddSeconds(1);
            _publisher.WorkerUnlinked(workerId);
            await PublishAsync();
        }

        Assert.Equal(100, ReadMetrics().Count);
        _now += _options.WorkerMetricsFileRetentionPeriod;
        _publisher.WorkerLinked("fresh-final", "http", true);
        _now = _now.AddSeconds(1);
        _publisher.WorkerUnlinked("fresh-final");
        await PublishAsync();
        Assert.Equal("fresh-final", Assert.Single(ReadMetrics()).InstanceId);

        if (hasLinkedWorker)
        {
            _publisher.WorkerLinked("current", "http", true);
        }

        _now = _now.AddMinutes(1);
        await PublishAsync();
        var records = ReadMetrics();
        Assert.Contains(records, record => string.Equals(record.InstanceId, "fresh-final", StringComparison.Ordinal));
        Assert.Equal(hasLinkedWorker ? 2 : 1, records.Count);
    }

    [Fact]
    public async Task FileRetention_UsesConfiguredPeriodAndPreservesExactBoundary()
    {
        _options.WorkerMetricsFileRetentionPeriod = TimeSpan.FromMinutes(2);
        _publisher.WorkerLinked("retired", "http", true);
        _now = _now.AddSeconds(1);
        _publisher.WorkerUnlinked("retired");
        await PublishAsync();
        _now += _options.WorkerMetricsFileRetentionPeriod;

        await PublishAsync();
        Assert.Single(ReadMetrics());
        _now = _now.AddTicks(1);
        await PublishAsync();
        Assert.Empty(ReadMetrics());
        Assert.Contains(_logger.GetLogMessages(), message =>
            message.Level == LogLevel.Debug &&
            message.FormattedMessage.Contains("Deleted 1 expired Worker metrics file", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FileRetention_NonSeparatedFlexDoesNotExpireWorkerFiles()
    {
        _publisher.WorkerLinked("retired", "http", true);
        _publisher.WorkerUnlinked("retired");
        await PublishAsync();
        using var publisher = CreateTraditionalPublisher();
        _now += _options.WorkerMetricsFileRetentionPeriod + TimeSpan.FromSeconds(1);

        await publisher.OnPublishMetrics(_now, ValueStopwatch.StartNew());

        Assert.Contains(ReadMetrics(), record => string.Equals(record.InstanceId, "retired", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AlwaysReady_WorkerLinkedAfterPublicationTimestampDoesNotMoveBoundaryBackwards()
    {
        DateTime captured = _now;
        _now = _now.AddSeconds(1);
        _publisher.WorkerLinked("worker", "http", true);

        await _publisher.OnPublishMetrics(captured, ValueStopwatch.StartNew());
        Assert.Empty(ReadMetrics());
        _now = _now.AddSeconds(2);
        await PublishAsync();

        Assert.Equal(2000, Assert.Single(ReadMetrics()).TotalTimeMS);
    }

    [Fact]
    public async Task AlwaysReady_WorkerLinkedAndUnlinkedAfterPublicationTimestampUsesActualLifetime()
    {
        DateTime captured = _now;
        _now = _now.AddSeconds(1);
        _publisher.WorkerLinked("worker", "http", true);
        _now = _now.AddSeconds(2);
        _publisher.WorkerUnlinked("worker");

        await _publisher.OnPublishMetrics(captured, ValueStopwatch.StartNew());
        Assert.Equal(2000, Assert.Single(ReadMetrics()).TotalTimeMS);
    }

    private FlexConsumptionMetricsPublisher CreateTraditionalPublisher()
    {
        Mock<IOptionsMonitor<StandbyOptions>> standby = new();
        standby.SetupGet(options => options.CurrentValue).Returns(new StandbyOptions());

        return new(new TestEnvironment(), standby.Object, Options.Create(_options),
            new TestLogger<FlexConsumptionMetricsPublisher>(), _fileSystem, _provider.Object);
    }

    private void StampNewFiles()
    {
        if (!_fileSystem.Directory.Exists(MetricsPath))
        {
            return;
        }

        foreach (string path in _fileSystem.Directory.GetFiles(MetricsPath))
        {
            if (_stampedFiles.Add(path))
            {
                _fileSystem.File.SetLastWriteTimeUtc(path, _now);
            }
        }
    }

    public void Dispose()
    {
        _publisher.Dispose();
    }

    private void Dispatch(string invocationId, string workerId)
    {
        Assert.True(_publisher.TryDispatchInvocation(invocationId, workerId, () => true));
    }

    private async Task PublishAsync()
    {
        await _publisher.OnPublishMetrics(_now, ValueStopwatch.StartNew());
        StampNewFiles();
    }

    private List<FlexConsumptionMetricsPublisherBase.Metrics> ReadMetrics()
    {
        return _fileSystem.Directory.Exists(MetricsPath)
            ? _fileSystem.Directory.GetFiles(MetricsPath).Select(path =>
                JsonConvert.DeserializeObject<FlexConsumptionMetricsPublisherBase.Metrics>(_fileSystem.File.ReadAllText(path))).ToList()
            : [];
    }
}
