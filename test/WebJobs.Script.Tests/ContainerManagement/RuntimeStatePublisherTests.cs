// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.ContainerManagement
{
    public class RuntimeStatePublisherTests
    {
        private static readonly TimeSpan ObservationWindow = TimeSpan.FromSeconds(3);

        private readonly TestRuntimeStateManager _stateManager = new();
        private readonly Mock<IMeshServiceClient> _mockMeshClient = new();

        [Fact]
        public async Task StateChange_AfterStart_PublishesSnapshot()
        {
            var published = new TaskCompletionSource<RuntimeState>(TaskCreationOptions.RunContinuationsAsynchronously);
            _mockMeshClient
                .Setup(c => c.PublishRuntimeState(It.IsAny<RuntimeState>()))
                .Callback<RuntimeState>(s => published.TrySetResult(s))
                .Returns(Task.CompletedTask);

            _stateManager.NextSnapshot = new RuntimeState { LinkedWorkerCount = 1, TotalRequestSlots = 16, TotalAvailableRequestSlots = 16 };

            using var publisher = CreatePublisher();
            await publisher.StartAsync(CancellationToken.None);

            _stateManager.RaiseStateChanged();

            var completed = await Task.WhenAny(published.Task, Task.Delay(ObservationWindow));
            Assert.Same(published.Task, completed);
            var snapshot = published.Task.Result;
            Assert.Equal(1, snapshot.LinkedWorkerCount);
            Assert.Equal(16, snapshot.TotalRequestSlots);
        }

        [Fact]
        public async Task StateChange_BeforeStart_DoesNotPublish()
        {
            _mockMeshClient
                .Setup(c => c.PublishRuntimeState(It.IsAny<RuntimeState>()))
                .Returns(Task.CompletedTask);

            using var publisher = CreatePublisher();

            _stateManager.RaiseStateChanged();

            await Task.Delay(TimeSpan.FromSeconds(1));

            _mockMeshClient.Verify(c => c.PublishRuntimeState(It.IsAny<RuntimeState>()), Times.Never);
        }

        [Fact]
        public async Task StateChange_AfterStop_DoesNotPublish()
        {
            _mockMeshClient
                .Setup(c => c.PublishRuntimeState(It.IsAny<RuntimeState>()))
                .Returns(Task.CompletedTask);

            using var publisher = CreatePublisher();
            await publisher.StartAsync(CancellationToken.None);
            await publisher.StopAsync(CancellationToken.None);

            _stateManager.RaiseStateChanged();

            await Task.Delay(TimeSpan.FromSeconds(1));

            _mockMeshClient.Verify(c => c.PublishRuntimeState(It.IsAny<RuntimeState>()), Times.Never);
        }

        [Fact]
        public async Task RapidStateChanges_CoalesceIntoSinglePublish()
        {
            int publishCount = 0;
            _mockMeshClient
                .Setup(c => c.PublishRuntimeState(It.IsAny<RuntimeState>()))
                .Callback(() => Interlocked.Increment(ref publishCount))
                .Returns(Task.CompletedTask);

            using var publisher = CreatePublisher();
            await publisher.StartAsync(CancellationToken.None);

            for (int i = 0; i < 20; i++)
            {
                _stateManager.RaiseStateChanged();
            }

            // The debounce window is ~500ms; allow some headroom.
            await Task.Delay(TimeSpan.FromSeconds(2));

            Assert.Equal(1, Volatile.Read(ref publishCount));
        }

        [Fact]
        public async Task PublishFailure_DoesNotThrowToStateManager()
        {
            _mockMeshClient
                .Setup(c => c.PublishRuntimeState(It.IsAny<RuntimeState>()))
                .ThrowsAsync(new InvalidOperationException("mesh down"));

            using var publisher = CreatePublisher();
            await publisher.StartAsync(CancellationToken.None);

            _stateManager.RaiseStateChanged();

            // Give the timer time to fire and the exception time to surface.
            await Task.Delay(TimeSpan.FromSeconds(2));

            // If the publisher propagated the exception out of the async void timer,
            // the process would have crashed. Reaching this line means it was swallowed.
            _mockMeshClient.Verify(c => c.PublishRuntimeState(It.IsAny<RuntimeState>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task StateChange_DuringSlowPublish_DoesNotCauseConcurrentPublish()
        {
            var firstEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int inFlight = 0;
            int maxInFlight = 0;

            _mockMeshClient
                .Setup(c => c.PublishRuntimeState(It.IsAny<RuntimeState>()))
                .Returns<RuntimeState>(async _ =>
                {
                    int now = Interlocked.Increment(ref inFlight);
                    InterlockedExtensions.Max(ref maxInFlight, now);
                    if (firstEntered.TrySetResult(true))
                    {
                        await releaseFirst.Task;
                    }
                    Interlocked.Decrement(ref inFlight);
                });

            using var publisher = CreatePublisher();
            await publisher.StartAsync(CancellationToken.None);

            _stateManager.RaiseStateChanged();

            // Wait for the first publish to enter (timer ~500ms + jitter).
            var entered = await Task.WhenAny(firstEntered.Task, Task.Delay(ObservationWindow));
            Assert.Same(firstEntered.Task, entered);

            // Raise a second change while the first publish is parked. The
            // gate must prevent a concurrent call; the drain loop must
            // schedule one follow-up publish after we release.
            _stateManager.RaiseStateChanged();

            // Give the debounce timer ample time to fire — without the gate
            // this would invoke PublishRuntimeState concurrently.
            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.Equal(1, Volatile.Read(ref inFlight));

            releaseFirst.TrySetResult(true);

            // Allow the drain loop to publish the second snapshot.
            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.Equal(1, Volatile.Read(ref maxInFlight));
            _mockMeshClient.Verify(c => c.PublishRuntimeState(It.IsAny<RuntimeState>()), Times.Exactly(2));
        }

        [Fact]
        public async Task StateChange_DuringSlowPublish_FollowUpPublishUsesLatestSnapshot()
        {
            var firstEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var snapshots = new System.Collections.Concurrent.ConcurrentQueue<RuntimeState>();

            _mockMeshClient
                .Setup(c => c.PublishRuntimeState(It.IsAny<RuntimeState>()))
                .Returns<RuntimeState>(async snapshot =>
                {
                    snapshots.Enqueue(snapshot);
                    if (firstEntered.TrySetResult(true))
                    {
                        await releaseFirst.Task;
                    }
                });

            _stateManager.NextSnapshot = new RuntimeState { LinkedWorkerCount = 1, TotalRequestSlots = 16, TotalAvailableRequestSlots = 16 };

            using var publisher = CreatePublisher();
            await publisher.StartAsync(CancellationToken.None);

            _stateManager.RaiseStateChanged();

            var entered = await Task.WhenAny(firstEntered.Task, Task.Delay(ObservationWindow));
            Assert.Same(firstEntered.Task, entered);

            // Update the snapshot, then raise — the drain loop must call
            // GetState() again and publish the *new* snapshot.
            _stateManager.NextSnapshot = new RuntimeState { LinkedWorkerCount = 2, TotalRequestSlots = 32, TotalAvailableRequestSlots = 30 };
            _stateManager.RaiseStateChanged();

            await Task.Delay(TimeSpan.FromMilliseconds(750));
            releaseFirst.TrySetResult(true);

            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.Equal(2, snapshots.Count);
            Assert.True(snapshots.TryDequeue(out var first));
            Assert.True(snapshots.TryDequeue(out var second));
            Assert.Equal(1, first.LinkedWorkerCount);
            Assert.Equal(2, second.LinkedWorkerCount);
            Assert.Equal(32, second.TotalRequestSlots);
        }

        private RuntimeStatePublisher CreatePublisher() =>
            new(_stateManager, _mockMeshClient.Object, NullLogger<RuntimeStatePublisher>.Instance);

        private sealed class TestRuntimeStateManager : IRuntimeStateManager
        {
            public event Action StateChanged;

            public RuntimeState NextSnapshot { get; set; } = new RuntimeState();

            public void RaiseStateChanged() => StateChanged?.Invoke();

            public RuntimeState GetState() => NextSnapshot;

            public void OnWorkerLinked(string workerId)
            {
            }

            public void OnWorkerUnlinked(string workerId)
            {
            }

            public void OnWorkerCapacityAvailable(string workerId, int slotCapacity)
            {
            }

            public void OnWorkerCapacityUnavailable(string workerId)
            {
            }

            public int AcquireSlots(int requestedSlotCount) => 0;

            public void ReleaseSlots(int count)
            {
            }

            public void SetStopping()
            {
            }
        }

        private static class InterlockedExtensions
        {
            public static void Max(ref int location, int value)
            {
                int initial;
                do
                {
                    initial = Volatile.Read(ref location);
                    if (value <= initial)
                    {
                        return;
                    }
                }
                while (Interlocked.CompareExchange(ref location, value, initial) != initial);
            }
        }
    }
}
