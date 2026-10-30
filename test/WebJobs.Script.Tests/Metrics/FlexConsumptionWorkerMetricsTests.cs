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
    private readonly TestLogger<FlexConsumptionMetricsPublisher> _logger = new();
    private readonly FlexConsumptionMetricsPublisher _publisher;
    private readonly FlexConsumptionMetricsPublisherOptions _options;
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
        _options.UseWorkerAttribution = false;
        _publisher.OnFunctionStarted("function", "invocation", _now);
        _now = _now.AddSeconds(1);
        _publisher.OnFunctionCompleted("function", "invocation", _now);
        await PublishAsync();

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
        _publisher.OnFunctionCompleted("function", "invocation", _now);
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
    public async Task CancelledBeforeDispatch_DoesNotPostOrBill()
    {
        using CancellationTokenSource cancellation = new();
        _publisher.WorkerLinked("worker");
        cancellation.Cancel();
        Mock<Func<bool>> dispatch = new();

        Assert.False(_publisher.TryDispatchInvocation("invocation", "worker", cancellation.Token, dispatch.Object));
        await PublishAsync();

        Assert.Empty(ReadMetrics());
        dispatch.Verify(post => post(), Times.Never);
    }

    [Fact]
    public async Task Completion_DisposesCancellationRegistrationAndCountsOnce()
    {
        using CancellationTokenSource cancellation = new();
        _publisher.WorkerLinked("worker");
        Dispatch("invocation", "worker", cancellation.Token);
        _now = _now.AddSeconds(1);
        _publisher.OnFunctionCompleted("function", "invocation", _now);
        _now = _now.AddSeconds(1);
        cancellation.Cancel();
        _publisher.OnFunctionCompleted("function", "invocation", _now);
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
            _publisher.OnFunctionCompleted("function", invocationId, _now);
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
    public async Task OnPublishMetrics_ConcurrentWorkersProduceIndependentFiles()
    {
        _publisher.WorkerLinked("worker-a");
        _publisher.WorkerLinked("worker-b");
        Dispatch("a1", "worker-a");
        Dispatch("b1", "worker-b");
        _now = _now.AddSeconds(1);
        _publisher.OnFunctionCompleted("function", "a1", _now);
        _now = _now.AddSeconds(1);
        _publisher.OnFunctionCompleted("function", "b1", _now);

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
        _publisher.OnFunctionCompleted("function", "first", _now);
        _now = _now.AddSeconds(1);
        _publisher.OnFunctionCompleted("function", "second", _now);

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
        _publisher.OnFunctionCompleted("function", "first", _now);
        Dispatch("second", "worker");
        _now = _now.AddMilliseconds(200);
        _publisher.OnFunctionCompleted("function", "second", _now);
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
                _publisher.TryDispatchInvocation("invocation", "worker", default, () => throw new InvalidOperationException()));
        }
        else
        {
            Assert.False(_publisher.TryDispatchInvocation("invocation", "worker", default, () => false));
        }

        await PublishAsync();
        Assert.Empty(ReadMetrics());

        Dispatch("invocation", "worker");
        _now = _now.AddSeconds(1);
        _publisher.OnFunctionCompleted("function", "invocation", _now);
        await PublishAsync();
        Assert.Equal(1, Assert.Single(ReadMetrics()).ExecutionCount);
    }

    [Fact]
    public async Task Cancellation_FinalizesActivityWithoutCompletionCallback()
    {
        using CancellationTokenSource cancellation = new();
        _publisher.WorkerLinked("worker");
        Dispatch("invocation", "worker", cancellation.Token);
        _now = _now.AddSeconds(1);
        cancellation.Cancel();
        _now = _now.AddSeconds(2);
        _publisher.OnFunctionCompleted("function", "invocation", _now);
        await PublishAsync();

        var metrics = Assert.Single(ReadMetrics());
        Assert.Equal(1, metrics.ExecutionCount);
        Assert.Equal(1000, metrics.ExecutionTimeMS);
        Assert.Equal(0, metrics.ActiveInvocationCount);
    }

    [Fact]
    public async Task CancellationDuringDispatch_FinalizesTheAcceptedInvocationOnce()
    {
        using CancellationTokenSource cancellation = new();
        _publisher.WorkerLinked("worker");

        Assert.True(_publisher.TryDispatchInvocation("invocation", "worker", cancellation.Token, () =>
        {
            _now = _now.AddSeconds(1);
            cancellation.Cancel();

            return true;
        }));
        _publisher.OnFunctionCompleted("function", "invocation", _now);
        await PublishAsync();

        var metrics = Assert.Single(ReadMetrics());
        Assert.Equal(1, metrics.ExecutionCount);
        Assert.Equal(1000, metrics.ExecutionTimeMS);
        Assert.Equal(0, metrics.ActiveInvocationCount);
    }

    [Fact]
    public async Task WorkerUnlinked_FinalizesOutstandingActivityAndStopsKeepAlives()
    {
        _publisher.WorkerLinked("worker");
        Dispatch("invocation", "worker");
        _now = _now.AddSeconds(1);
        _publisher.WorkerUnlinked("worker");
        _now = _now.AddSeconds(10);
        _publisher.OnFunctionCompleted("function", "invocation", _now);
        await PublishAsync();
        _now = _now.AddMinutes(1);
        await PublishAsync();

        var metrics = Assert.Single(ReadMetrics());
        Assert.Equal(1000, metrics.TotalTimeMS);
        Assert.Equal(1000, metrics.ExecutionTimeMS);
        Assert.Equal(1, metrics.ExecutionCount);
        Assert.False(_publisher.TryDispatchInvocation("late", "worker", default, () => true));
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
        _publisher.OnFunctionCompleted("function", "unattributed", _now);
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

    public void Dispose()
    {
        _publisher.Dispose();
    }

    private void Dispatch(string invocationId, string workerId, CancellationToken cancellationToken = default)
    {
        Assert.True(_publisher.TryDispatchInvocation(invocationId, workerId, cancellationToken, () => true));
    }

    private Task PublishAsync() => _publisher.OnPublishMetrics(_now, ValueStopwatch.StartNew());

    private List<FlexConsumptionMetricsPublisher.Metrics> ReadMetrics()
    {
        return _fileSystem.Directory.Exists(MetricsPath)
            ? _fileSystem.Directory.GetFiles(MetricsPath).Select(path =>
                JsonConvert.DeserializeObject<FlexConsumptionMetricsPublisher.Metrics>(_fileSystem.File.ReadAllText(path))).ToList()
            : [];
    }
}
