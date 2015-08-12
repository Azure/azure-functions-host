// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Singleton
{
    public class SingletonListenerTests
    {
        private readonly Mock<SingletonManager> _mockSingletonManager;
        private readonly Mock<IListener> _mockInnerListener;
        private readonly SingletonListener _listener;
        private readonly SingletonAttribute _attribute;
        private readonly string _lockId;

        public SingletonListenerTests()
        {
            MethodInfo methodInfo = this.GetType().GetMethod("TestJob", BindingFlags.Static|BindingFlags.NonPublic);
            _attribute = new SingletonAttribute();
            _mockSingletonManager = new Mock<SingletonManager>(MockBehavior.Strict);
            _mockInnerListener = new Mock<IListener>(MockBehavior.Strict);
            _listener = new SingletonListener(methodInfo, _attribute, _mockSingletonManager.Object, _mockInnerListener.Object);
            _lockId = SingletonManager.FormatLockId(methodInfo, _attribute.Scope) + ".Listener";
        }

        [Fact]
        public async Task StartAsync_StartsListener_WhenLockAcquired()
        {
            CancellationToken cancellationToken = new CancellationToken();
            SingletonManager.SingletonLockHandle lockHandle = new SingletonManager.SingletonLockHandle();
            _mockSingletonManager.Setup(p => p.TryLockAsync(_lockId, null, _attribute, cancellationToken)).ReturnsAsync(lockHandle);
            _mockInnerListener.Setup(p => p.StartAsync(cancellationToken)).Returns(Task.FromResult(true));

            await _listener.StartAsync(cancellationToken);

            _mockSingletonManager.VerifyAll();
            _mockInnerListener.VerifyAll();
        }

        [Fact]
        public async Task StartAsync_DoesNotStartListener_WhenLockCannotBeAcquired()
        {
            CancellationToken cancellationToken = new CancellationToken();
            _mockSingletonManager.Setup(p => p.TryLockAsync(_lockId, null, _attribute, cancellationToken)).ReturnsAsync(null);

            await _listener.StartAsync(cancellationToken);

            _mockSingletonManager.VerifyAll();
        }

        [Fact]
        public async Task StartAsync_DefaultsAcquisitionTimeout()
        {
            CancellationToken cancellationToken = new CancellationToken();
            SingletonManager.SingletonLockHandle lockHandle = new SingletonManager.SingletonLockHandle();
            _mockSingletonManager.Setup(p => p.TryLockAsync(_lockId, null, _attribute, cancellationToken))
                .Callback<string, string, SingletonAttribute, CancellationToken>(
                    (mockLockId, mockInstanceId, mockAttribute, mockCancellationToken) => 
                    {
                        Assert.Equal(15, mockAttribute.LockAcquisitionTimeout);
                    }) 
                .ReturnsAsync(lockHandle);
            _mockInnerListener.Setup(p => p.StartAsync(cancellationToken)).Returns(Task.FromResult(true));

            Assert.Null(_attribute.LockAcquisitionTimeout);
            await _listener.StartAsync(cancellationToken);
            Assert.Equal(15, _attribute.LockAcquisitionTimeout);

            _mockSingletonManager.VerifyAll();
            _mockInnerListener.VerifyAll();
        }

        [Fact]
        public async Task StopAsync_Noops_WhenLockNotAquired()
        {
            CancellationToken cancellationToken = new CancellationToken();
            await _listener.StopAsync(cancellationToken);
        }

        [Fact]
        public async Task StopAsync_WhenLockAcquired_ReleasesLock_AndStopsListener()
        {
            CancellationToken cancellationToken = new CancellationToken();
            SingletonManager.SingletonLockHandle lockHandle = new SingletonManager.SingletonLockHandle();
            _mockSingletonManager.Setup(p => p.TryLockAsync(_lockId, null, _attribute, cancellationToken)).ReturnsAsync(lockHandle);
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
        public void Dispose_DisposesListener()
        {
            _mockInnerListener.Setup(p => p.Dispose());
            _listener.Dispose();
        }

        private static void TestJob()
        {
        }
    }
}
