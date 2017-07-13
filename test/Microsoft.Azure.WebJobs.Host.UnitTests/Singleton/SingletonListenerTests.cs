// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Moq;
using Xunit;
using SingletonLockHandle = Microsoft.Azure.WebJobs.Host.BlobLeaseDistributedLockManager.SingletonLockHandle;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Singleton
{
    public class SingletonListenerTests
    {
        private readonly string testHostId = "testhostid";
        private readonly SingletonConfiguration _config;
        private readonly Mock<SingletonManager> _mockSingletonManager;
        private readonly Mock<IListener> _mockInnerListener;
        private readonly SingletonListener _listener;
        private readonly SingletonAttribute _attribute;
        private readonly string _lockId;

        public SingletonListenerTests()
        {
            MethodInfo methodInfo = this.GetType().GetMethod("TestJob", BindingFlags.Static | BindingFlags.NonPublic);
            var descriptor = FunctionIndexer.FromMethod(methodInfo);
            _attribute = new SingletonAttribute();
            _config = new SingletonConfiguration
            {
                LockPeriod = TimeSpan.FromSeconds(20)
            };
            _mockSingletonManager = new Mock<SingletonManager>(MockBehavior.Strict, null, null, null, null, null, new FixedHostIdProvider(testHostId), null);
            _mockSingletonManager.SetupGet(p => p.Config).Returns(_config);
            _mockInnerListener = new Mock<IListener>(MockBehavior.Strict);

            _listener = new SingletonListener(descriptor, _attribute, _mockSingletonManager.Object, _mockInnerListener.Object,
                new TestTraceWriter(System.Diagnostics.TraceLevel.Verbose), null);
            _lockId = SingletonManager.FormatLockId(descriptor, SingletonScope.Function, testHostId, _attribute.ScopeId) + ".Listener";
        }

        [Fact]
        public async Task StartAsync_StartsListener_WhenLockAcquired()
        {
            CancellationToken cancellationToken = new CancellationToken();
            var lockHandle = new RenewableLockHandle(new SingletonLockHandle(), null);
            _mockSingletonManager.Setup(p => p.TryLockAsync(_lockId, null, _attribute, cancellationToken, false))
                .ReturnsAsync(lockHandle);
            _mockInnerListener.Setup(p => p.StartAsync(cancellationToken)).Returns(Task.FromResult(true));

            await _listener.StartAsync(cancellationToken);

            Assert.Null(_listener.LockTimer);

            _mockSingletonManager.VerifyAll();
            _mockInnerListener.VerifyAll();
        }

        [Fact]
        public async Task StartAsync_DoesNotStartListener_WhenLockCannotBeAcquired()
        {
            CancellationToken cancellationToken = new CancellationToken();
            _mockSingletonManager.Setup(p => p.TryLockAsync(_lockId, null, _attribute, cancellationToken, false))
                .ReturnsAsync((RenewableLockHandle)null);

            await _listener.StartAsync(cancellationToken);

            // verify that the LockTimer has been started
            Assert.NotNull(_listener.LockTimer);
            Assert.True(_listener.LockTimer.AutoReset);
            Assert.True(_listener.LockTimer.Enabled);
            Assert.Equal(_config.ListenerLockRecoveryPollingInterval.TotalMilliseconds, _listener.LockTimer.Interval);

            _mockSingletonManager.VerifyAll();
        }

        [Fact]
        public async Task StartAsync_DoesNotStartLockTimer_WhenPollingIntervalSetToInfinite()
        {
            // we expect the "retry" parameter passed to TryLockAync to be "true"
            // when recovery polling is turned off
            _config.ListenerLockRecoveryPollingInterval = TimeSpan.MaxValue;

            CancellationToken cancellationToken = new CancellationToken();
            _mockSingletonManager.Setup(p => p.TryLockAsync(_lockId, null, _attribute, cancellationToken, true))
                .ReturnsAsync((RenewableLockHandle)null);

            await _listener.StartAsync(cancellationToken);

            // verify that the LockTimer has NOT been started
            Assert.Null(_listener.LockTimer);

            _mockSingletonManager.VerifyAll();
        }

        [Fact]
        public async Task TryAcquireLock_WhenLockAcquired_StopsLockTimerAndStartsListener()
        {
            _listener.LockTimer = new System.Timers.Timer
            {
                Interval = 30 * 1000
            };
            _listener.LockTimer.Start();

            RenewableLockHandle lockHandle = new RenewableLockHandle(new SingletonLockHandle(), null);
            _mockSingletonManager.Setup(p => p.TryLockAsync(_lockId, null, _attribute, CancellationToken.None, false))
                .ReturnsAsync(lockHandle);

            _mockInnerListener.Setup(p => p.StartAsync(CancellationToken.None)).Returns(Task.FromResult(true));

            await _listener.TryAcquireLock();

            Assert.Null(_listener.LockTimer);
        }

        [Fact]
        public async Task TryAcquireLock_LockNotAcquired_DoesNotStopLockTimer()
        {
            _listener.LockTimer = new System.Timers.Timer
            {
                Interval = 30 * 1000
            };
            _listener.LockTimer.Start();

            _mockSingletonManager.Setup(p => p.TryLockAsync(_lockId, null, _attribute, CancellationToken.None, false))
                .ReturnsAsync((RenewableLockHandle)null);

            Assert.True(_listener.LockTimer.Enabled);

            await _listener.TryAcquireLock();

            Assert.True(_listener.LockTimer.Enabled);
        }

        [Fact]
        public async Task StopAsync_WhenNotStarted_Noops()
        {
            CancellationToken cancellationToken = new CancellationToken();
            await _listener.StopAsync(cancellationToken);
        }

        [Fact]
        public async Task StopAsync_WhenLockNotAcquired_StopsLockTimer()
        {
            CancellationToken cancellationToken = new CancellationToken();
            _mockSingletonManager.Setup(p => p.TryLockAsync(_lockId, null, _attribute, cancellationToken, false))
                .ReturnsAsync((RenewableLockHandle)null);

            await _listener.StartAsync(cancellationToken);

            Assert.True(_listener.LockTimer.Enabled);

            await _listener.StopAsync(cancellationToken);

            Assert.False(_listener.LockTimer.Enabled);

            _mockSingletonManager.VerifyAll();
            _mockInnerListener.VerifyAll();
        }

        [Fact]
        public async Task StopAsync_WhenLockAcquired_ReleasesLock_AndStopsListener()
        {
            CancellationToken cancellationToken = new CancellationToken();
            var lockHandle = new RenewableLockHandle(new SingletonLockHandle(), null);
            _mockSingletonManager.Setup(p => p.TryLockAsync(_lockId, null, _attribute, cancellationToken, false))
                .ReturnsAsync(lockHandle);
            _mockInnerListener.Setup(p => p.StartAsync(cancellationToken)).Returns(Task.FromResult(true));

            await _listener.StartAsync(cancellationToken);

            _mockSingletonManager.Setup(p => p.ReleaseLockAsync(lockHandle, cancellationToken)).Returns(Task.FromResult(true));
            _mockInnerListener.Setup(p => p.StopAsync(cancellationToken)).Returns(Task.FromResult(true));

            await _listener.StopAsync(cancellationToken);

            _mockSingletonManager.VerifyAll();
            _mockInnerListener.VerifyAll();
        }

        [Fact]
        public void Cancel_CancelsListener()
        {
            _mockInnerListener.Setup(p => p.Cancel());
            _listener.Cancel();
        }

        [Fact]
        public void Cancel_StopsLockTimer()
        {
            _listener.LockTimer = new System.Timers.Timer
            {
                Interval = 30 * 1000
            };
            _listener.LockTimer.Start();

            _mockInnerListener.Setup(p => p.Cancel());
            _listener.Cancel();

            Assert.False(_listener.LockTimer.Enabled);
        }

        [Fact]
        public void Dispose_DisposesListener()
        {
            _mockInnerListener.Setup(p => p.Dispose());
            _listener.Dispose();
        }

        [Fact]
        public async Task Dispose_WhenLockAcquired_ReleasesLock()
        {
            CancellationToken cancellationToken = new CancellationToken();
            var lockHandle = new RenewableLockHandle(new SingletonLockHandle(), null);
            _mockSingletonManager.Setup(p => p.TryLockAsync(_lockId, null, _attribute, cancellationToken, false))
                .ReturnsAsync(lockHandle);
            _mockInnerListener.Setup(p => p.StartAsync(cancellationToken)).Returns(Task.FromResult(true));

            await _listener.StartAsync(cancellationToken);

            _mockInnerListener.Setup(p => p.Dispose());
            _mockSingletonManager.Setup(p => p.ReleaseLockAsync(lockHandle, cancellationToken)).Returns(Task.FromResult(true));

            _listener.Dispose();

            _mockSingletonManager.VerifyAll();
            _mockInnerListener.VerifyAll();
        }

        [Fact]
        public void Dispose_DisposesLockTimer()
        {
            _listener.LockTimer = new System.Timers.Timer
            {
                Interval = 30 * 1000
            };
            _listener.LockTimer.Start();

            _mockInnerListener.Setup(p => p.Dispose());
            _listener.Dispose();

            Assert.False(_listener.LockTimer.Enabled);
        }

        private static void TestJob()
        {
        }
    }
}
