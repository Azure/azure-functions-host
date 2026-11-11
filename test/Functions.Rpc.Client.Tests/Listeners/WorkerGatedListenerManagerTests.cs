// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed class WorkerGatedListenerManagerTests : IDisposable
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private readonly FakeTimeProvider _timeProvider = new();
    private readonly Mock<IWorkerChannelRegistry> _registry = new();
    private readonly Lock _channelsLock = new();
    private readonly List<WorkerChannel> _channels = [];
    private readonly List<WorkerChannel> _readyChannels = [];
    private readonly WorkerGatedListenerManager _manager;
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<IReadOnlyList<WorkerChannel>> _readyChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public WorkerGatedListenerManagerTests()
    {
        _registry.Setup(r => r.GetChangeTask()).Returns(() =>
        {
            lock (_channelsLock)
            {
                return _changed.Task;
            }
        });
        _registry.Setup(r => r.GetInitializedChannels()).Returns(() =>
        {
            lock (_channelsLock)
            {
                return [.. _channels];
            }
        });
        _registry.Setup(r => r.GetReadyChannelsAsync()).Returns(() =>
        {
            lock (_channelsLock)
            {
                List<WorkerChannel> readyChannels = [];
                foreach (WorkerChannel channel in _readyChannels)
                {
                    if (channel is not RpcClientWorkerChannel client || !client.IsTerminated)
                    {
                        readyChannels.Add(channel);
                    }
                }

                if (readyChannels.Count > 0)
                {
                    return ValueTask.FromResult<IReadOnlyList<WorkerChannel>>(readyChannels);
                }

                if (_readyChanged.Task.IsCompleted)
                {
                    _readyChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }

                return new(_readyChanged.Task);
            }
        });
        _manager = new(_registry.Object, _timeProvider, NullLogger<WorkerGatedListenerManager>.Instance);
    }

    public void Dispose() => _manager.Dispose();

    [Fact]
    public async Task Reconcile_StartsAndStopsListenerWithMatchingWorker()
    {
        await using ClientWorkerChannelTestHarness worker = await CreateReadyWorkerAsync("function:TestFunction");
        TestListener initial = new();
        TestListener second = new();
        using WorkerGatedListener listener = CreateListener(initial, second);
        await listener.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        Assert.False(initial.Started.Task.IsCompleted);

        SetChannels(worker.Channel);
        await initial.Started.Task.WaitAsync(TestTimeout);

        SetChannels();
        await initial.Stopped.Task.WaitAsync(TestTimeout);

        SetChannels(worker.Channel);
        await second.Started.Task.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task Reconcile_IgnoresWorkerForOtherGroup()
    {
        await using ClientWorkerChannelTestHarness other = await CreateReadyWorkerAsync("function:Other");
        await using ClientWorkerChannelTestHarness matching = await CreateReadyWorkerAsync("function:TestFunction");
        TestListener initial = new();
        using WorkerGatedListener listener = CreateListener(initial);
        await listener.StartAsync(CancellationToken.None);

        SetChannels(other.Channel);
        await Task.Delay(50);
        Assert.False(initial.Started.Task.IsCompleted);

        SetChannels(other.Channel, matching.Channel);
        await initial.Started.Task.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task Reconcile_DoesNotStartForInitializedButNotReadyWorker()
    {
        await using ClientWorkerChannelTestHarness worker = await CreateReadyWorkerAsync("function:TestFunction");
        TestListener initial = new();
        using WorkerGatedListener listener = CreateListener(initial);
        SetInitializedChannels(worker.Channel);
        await listener.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        Assert.False(initial.Started.Task.IsCompleted);

        SetChannels(worker.Channel);
        await initial.Started.Task.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task Reconcile_TerminalWorkerSignalStopsListenerWithoutRegistryChange()
    {
        await using ClientWorkerChannelTestHarness worker = await CreateReadyWorkerAsync("function:TestFunction");
        TestListener initial = new();
        using WorkerGatedListener listener = CreateListener(initial);
        SetChannels(worker.Channel);
        await listener.StartAsync(CancellationToken.None);
        await initial.Started.Task.WaitAsync(TestTimeout);

        await worker.DisposeAsync();

        await initial.Stopped.Task.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task Reconcile_CancelsInProgressStartWhenLastWorkerLeaves()
    {
        await using ClientWorkerChannelTestHarness worker = await CreateReadyWorkerAsync("function:TestFunction");
        TaskCompletionSource startGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TestListener initial = new() { StartGate = startGate.Task };
        TestListener second = new();
        using WorkerGatedListener listener = CreateListener(initial, second);
        SetChannels(worker.Channel);
        await listener.StartAsync(CancellationToken.None);
        await initial.Started.Task.WaitAsync(TestTimeout);

        SetChannels();
        await initial.Stopped.Task.WaitAsync(TestTimeout);
        Assert.False(listener.IsListening);

        SetChannels(worker.Channel);
        await second.Started.Task.WaitAsync(TestTimeout);
        Assert.True(listener.IsListening);
    }

    [Fact]
    public async Task Reconcile_FailedStop_RetriesBeforeStartingReplacement()
    {
        await using ClientWorkerChannelTestHarness worker = await CreateReadyWorkerAsync("function:TestFunction");
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource failedStop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource replacementStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int stopAttempts = 0;
        Mock<IListener> initial = new();
        initial.Setup(l => l.StartAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            started.TrySetResult();
            return Task.CompletedTask;
        });
        initial.Setup(l => l.StopAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            if (Interlocked.Increment(ref stopAttempts) == 1)
            {
                failedStop.TrySetResult();
                return Task.FromException(new InvalidOperationException("stop failed"));
            }

            return Task.CompletedTask;
        });
        Mock<IListener> replacement = new();
        replacement.Setup(l => l.StartAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            replacementStarted.TrySetResult();
            return Task.CompletedTask;
        });
        Mock<IListenerFactory> factory = new();
        factory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(replacement.Object);
        using WorkerGatedListener listener = new(initial.Object, factory.Object, "TestFunction", "function:TestFunction", false,
            _manager, _timeProvider, NullLogger.Instance);
        SetChannels(worker.Channel);
        await listener.StartAsync(CancellationToken.None);
        await started.Task.WaitAsync(TestTimeout);

        SetChannels();
        await failedStop.Task.WaitAsync(TestTimeout);
        SetChannels(worker.Channel);
        await replacementStarted.Task.WaitAsync(TestTimeout);

        initial.Verify(l => l.StopAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        initial.Verify(l => l.Dispose(), Times.Never);
    }

    [Fact]
    public async Task Reconcile_SlowTimerCancellation_DoesNotBlockOtherListeners()
    {
        await using ClientWorkerChannelTestHarness timerWorker = await CreateReadyWorkerAsync("function:TestFunction");
        await using ClientWorkerChannelTestHarness otherWorker = await CreateReadyWorkerAsync("function:Other");
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource finishStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource cancelEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource cancelExited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using ManualResetEventSlim finishCancel = new();
        Mock<IListener> timer = new();
        timer.Setup(l => l.StartAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            started.TrySetResult();
            return finishStart.Task;
        });
        timer.Setup(l => l.Cancel()).Callback(() =>
        {
            cancelEntered.TrySetResult();
            finishCancel.Wait();
            cancelExited.TrySetResult();
        });
        TestListener other = new();
        using WorkerGatedListener timerListener = new(timer.Object, Mock.Of<IListenerFactory>(), "TestFunction", "function:TestFunction",
            false, _manager, _timeProvider, NullLogger.Instance, cancelDuringStart: true);
        using WorkerGatedListener otherListener = CreateListener(other, "function:Other");
        await timerListener.StartAsync(CancellationToken.None);
        await otherListener.StartAsync(CancellationToken.None);
        SetChannels(timerWorker.Channel);
        await started.Task.WaitAsync(TestTimeout);

        try
        {
            SetChannels();
            await cancelEntered.Task.WaitAsync(TestTimeout);
            SetChannels(otherWorker.Channel);
            await other.Started.Task.WaitAsync(TestTimeout);
        }
        finally
        {
            finishCancel.Set();
            finishStart.TrySetResult();
            if (cancelEntered.Task.IsCompleted)
            {
                await cancelExited.Task.WaitAsync(TestTimeout);
            }
        }
    }

    [Fact]
    public async Task Reconcile_SerializesStartOnlyListenerStarts()
    {
        await using ClientWorkerChannelTestHarness worker = await CreateReadyWorkerAsync("function:TestFunction");
        TaskCompletionSource firstStartGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource secondStartGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TestListener first = new() { StartGate = firstStartGate.Task };
        TestListener second = new() { StartGate = secondStartGate.Task };
        using WorkerGatedListener firstListener = CreateStartOnlyListener(first);
        using WorkerGatedListener secondListener = CreateStartOnlyListener(second);
        SetChannels(worker.Channel);
        await firstListener.StartAsync(CancellationToken.None);
        await secondListener.StartAsync(CancellationToken.None);

        await Task.WhenAny(first.Started.Task, second.Started.Task).WaitAsync(TestTimeout);
        await Task.Delay(50);
        Assert.NotEqual(first.Started.Task.IsCompleted, second.Started.Task.IsCompleted);

        if (first.Started.Task.IsCompleted)
        {
            firstStartGate.SetResult();
            await second.Started.Task.WaitAsync(TestTimeout);
        }
        else
        {
            secondStartGate.SetResult();
            await first.Started.Task.WaitAsync(TestTimeout);
        }
    }

    [Fact]
    public async Task Reconcile_SharedBlobGate_PausesDuringStartAndIgnoresStaleCompletion()
    {
        await using ClientWorkerChannelTestHarness worker = await CreateReadyWorkerAsync("function:TestFunction");
        BlobListenerQueueGateTests.ICommand command = DispatchProxy.Create<BlobListenerQueueGateTests.ICommand, BlobListenerQueueGate>();
        BlobListenerQueueGate gate = (BlobListenerQueueGate)command;
        gate.Initialize(_ => Task.FromResult(42), NullLogger.Instance);
        TaskCompletionSource finishStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TestListener first = new() { StartGate = finishStart.Task };
        TestListener second = new();
        using WorkerGatedListener firstListener = CreateStartOnlyListener(first, gate);
        using WorkerGatedListener secondListener = CreateStartOnlyListener(second, gate);
        try
        {
            SetChannels(worker.Channel);
            await firstListener.StartAsync(CancellationToken.None);
            await first.Started.Task.WaitAsync(TestTimeout);
            await secondListener.StartAsync(CancellationToken.None);
            Assert.Equal(42, await command.ExecuteAsync(CancellationToken.None).WaitAsync(TestTimeout));

            SetChannels();
            await WaitForAsync(() =>
            {
                using CancellationTokenSource probe = new();
                Task<int> poll = command.ExecuteAsync(probe.Token);
                bool paused = !poll.IsCompleted;
                probe.Cancel();
                return paused;
            });

            using CancellationTokenSource cancellation = new();
            Task<int> pending = command.ExecuteAsync(cancellation.Token);
            finishStart.SetResult();
            await WaitForAsync(() => firstListener.IsListening);
            await Task.Delay(50);
            Assert.False(pending.IsCompleted);
            Assert.False(first.Stopped.Task.IsCompleted);
            Assert.False(second.Started.Task.IsCompleted);

            SetChannels(worker.Channel);
            Assert.Equal(42, await pending.WaitAsync(TestTimeout));
            await second.Started.Task.WaitAsync(TestTimeout);
        }
        finally
        {
            finishStart.TrySetResult();
        }
    }

    [Fact]
    public async Task Reconcile_FailedStart_RetriesAfterBackoff()
    {
        await using ClientWorkerChannelTestHarness worker = await CreateReadyWorkerAsync("function:TestFunction");
        TestListener initial = new() { StartException = new InvalidOperationException("boom") };
        TestListener second = new();
        SetChannels(worker.Channel);
        using WorkerGatedListener listener = CreateListener(initial, second);

        await listener.StartAsync(CancellationToken.None);
        await initial.Started.Task.WaitAsync(TestTimeout);
        await Task.Delay(50);
        Assert.False(second.Started.Task.IsCompleted);

        _timeProvider.Advance(WorkerGatedListenerManager.InitialRetryDelay);
        await WaitForAsync(() =>
        {
            // The retry timer may be scheduled after the first advance; keep advancing until it fires.
            _timeProvider.Advance(WorkerGatedListenerManager.InitialRetryDelay);
            return second.Started.Task.IsCompleted;
        });
        Assert.True(initial.Stopped.Task.IsCompleted);
    }

    [Fact]
    public async Task Reconcile_SlowStop_DoesNotBlockOtherListeners()
    {
        await using ClientWorkerChannelTestHarness slowWorker = await CreateReadyWorkerAsync("function:TestFunction");
        await using ClientWorkerChannelTestHarness otherWorker = await CreateReadyWorkerAsync("function:Other");
        TaskCompletionSource stopGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TestListener slow = new() { StopGate = stopGate.Task };
        TestListener other = new();
        using WorkerGatedListener slowListener = CreateListener(slow);
        using WorkerGatedListener otherListener = CreateListener(other, "function:Other");
        await slowListener.StartAsync(CancellationToken.None);
        await otherListener.StartAsync(CancellationToken.None);

        SetChannels(slowWorker.Channel);
        await slow.Started.Task.WaitAsync(TestTimeout);

        SetChannels();
        await slow.Stopped.Task.WaitAsync(TestTimeout);

        SetChannels(otherWorker.Channel);
        await other.Started.Task.WaitAsync(TestTimeout);

        stopGate.SetResult();
    }

    [Fact]
    public async Task Dispose_EndsReconcileLoop()
    {
        using WorkerGatedListener listener = CreateListener(new TestListener());
        await listener.StartAsync(CancellationToken.None);

        _manager.Dispose();

        await _manager.ReconcileTask.WaitAsync(TestTimeout);
        Assert.Throws<ObjectDisposedException>(() => _manager.Register(listener));
    }

    private WorkerGatedListener CreateListener(TestListener initial, params TestListener[] generations) =>
        CreateListener(initial, "function:TestFunction", generations);

    private WorkerGatedListener CreateStartOnlyListener(TestListener initial, BlobListenerQueueGate blobQueueGate = null) =>
        new(initial, Mock.Of<IListenerFactory>(), "TestFunction", "function:TestFunction", startOnly: true, _manager,
            _timeProvider, NullLogger.Instance, blobQueueGate: blobQueueGate);

    private WorkerGatedListener CreateListener(TestListener initial, string functionGroup, params TestListener[] generations)
    {
        Queue<TestListener> pending = new(generations);
        Mock<IListenerFactory> factory = new();
        factory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => pending.Dequeue());
        return new(initial, factory.Object, "TestFunction", functionGroup, startOnly: false, _manager, _timeProvider, NullLogger.Instance);
    }

    private void SetChannels(params WorkerChannel[] channels)
        => SetChannels(channels, channels);

    private void SetInitializedChannels(params WorkerChannel[] channels)
        => SetChannels(channels, []);

    private void SetChannels(WorkerChannel[] channels, WorkerChannel[] readyChannels)
    {
        TaskCompletionSource changed;
        lock (_channelsLock)
        {
            _channels.Clear();
            _channels.AddRange(channels);
            _readyChannels.Clear();
            _readyChannels.AddRange(readyChannels);
            if (_readyChannels.Count > 0)
            {
                _readyChanged.TrySetResult(_readyChannels.ToArray());
            }
            else
            {
                _readyChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            changed = _changed;
            _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        changed.TrySetResult();
    }

    private static async Task<ClientWorkerChannelTestHarness> CreateReadyWorkerAsync(string functionGroup)
    {
        ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync(functionGroup, functionGroupName: functionGroup);
        FunctionMetadata function = new() { Name = "TestFunction", Language = "external" };
        worker.Channel.SetupFunctionInvocationBuffers([function]);
        worker.Channel.SendFunctionLoadRequests(new ManagedDependencyOptions(), functionTimeout: null);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        await worker.SendFunctionLoadResponseAsync(function.GetFunctionId());
        await WaitForAsync(() => worker.Channel.IsReadyForInvocations);
        return worker;
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TestTimeout);
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class TestListener : IListener
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Exception StartException { get; init; }

        public Task StartGate { get; init; } = Task.CompletedTask;

        public Task StopGate { get; init; } = Task.CompletedTask;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            if (StartException is not null)
            {
                throw StartException;
            }

            await StartGate.WaitAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Stopped.TrySetResult();
            return StopGate;
        }

        public void Cancel()
        {
        }

        public void Dispose()
        {
        }
    }
}
