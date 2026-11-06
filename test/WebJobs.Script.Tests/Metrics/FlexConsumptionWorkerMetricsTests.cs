// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
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
        Assert.Equal(1, metrics.ExecutionCount);
        Assert.Equal(1000, metrics.ExecutionTimeMS);
    }

    [Fact]
    public async Task Dispatch_WaitingForWorkerDoesNotMeterRuntimePreparation()
    {
        _publisher.WorkerLinked("worker");
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
        _publisher.WorkerLinked("first");
        _publisher.WorkerLinked("second");
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
        _publisher.WorkerLinked("worker");
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
        _publisher.WorkerLinked("worker");
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
        _publisher.WorkerLinked(workerId);
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
        _publisher.WorkerLinked("worker-a");
        _publisher.WorkerLinked("worker-b");
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
    public async Task WorkerUnlinked_DoesNotCountLateSuccess()
    {
        using CancellationTokenSource cancellation = new();
        _publisher.WorkerLinked("worker");
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
        _publisher.WorkerLinked("worker-a");
        _publisher.WorkerLinked("worker-b");
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
        _publisher.WorkerLinked("worker");
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
        _publisher.WorkerLinked("worker");
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
        _publisher.WorkerLinked("worker");
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
        _publisher.WorkerLinked("worker");
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
        _publisher.WorkerLinked("worker");
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
        _publisher.WorkerLinked("worker");

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
        _publisher.WorkerLinked("worker");
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
        _publisher.IsAlwaysReady = true;
        _publisher.WorkerLinked("first");
        _now = _now.AddSeconds(1);
        _publisher.WorkerLinked("second");
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
        _publisher.WorkerLinked("worker");
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
        _publisher.WorkerLinked("worker-a");
        _publisher.WorkerLinked("worker-b");
        _publisher.IsAlwaysReady = true;
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
        _publisher.IsAlwaysReady = true;
        for (int i = 0; i < 100; i++)
        {
            string workerId = $"retired-{i}";
            _publisher.WorkerLinked(workerId);
            _now = _now.AddSeconds(1);
            _publisher.WorkerUnlinked(workerId);
            await PublishAsync();
        }

        Assert.Equal(100, ReadMetrics().Count);
        _now += _options.WorkerMetricsFileRetentionPeriod;
        _publisher.WorkerLinked("fresh-final");
        _now = _now.AddSeconds(1);
        _publisher.WorkerUnlinked("fresh-final");
        await PublishAsync();
        Assert.Equal("fresh-final", Assert.Single(ReadMetrics()).InstanceId);

        if (hasLinkedWorker)
        {
            _publisher.WorkerLinked("current");
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
        _publisher.IsAlwaysReady = true;
        _publisher.WorkerLinked("retired");
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
        _publisher.IsAlwaysReady = true;
        _publisher.WorkerLinked("retired");
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
        _publisher.IsAlwaysReady = true;
        DateTime captured = _now;
        _now = _now.AddSeconds(1);
        _publisher.WorkerLinked("worker");

        await _publisher.OnPublishMetrics(captured, ValueStopwatch.StartNew());
        Assert.Empty(ReadMetrics());
        _now = _now.AddSeconds(2);
        await PublishAsync();

        Assert.Equal(2000, Assert.Single(ReadMetrics()).TotalTimeMS);
    }

    [Fact]
    public async Task AlwaysReady_WorkerLinkedAndUnlinkedAfterPublicationTimestampUsesActualLifetime()
    {
        _publisher.IsAlwaysReady = true;
        DateTime captured = _now;
        _now = _now.AddSeconds(1);
        _publisher.WorkerLinked("worker");
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
