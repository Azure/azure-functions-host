// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Singleton
{
    public class SingletonLockTests
    {
        private const string TestLockId = "testid";
        private const string TestInstanceId = "testinstance";

        [Fact]
        public void Constructor_SetsExpectedValues()
        {
            SingletonAttribute attribute = new SingletonAttribute();
            Mock<SingletonManager> mockSingletonManager = new Mock<SingletonManager>(MockBehavior.Strict);
            SingletonLock singletonLock = new SingletonLock(TestLockId, TestInstanceId, attribute, mockSingletonManager.Object);

            Assert.Equal(TestLockId, singletonLock.Id);
            Assert.Equal(TestInstanceId, singletonLock.FunctionId);
            Assert.Null(singletonLock.AcquireStartTime);
            Assert.Null(singletonLock.AcquireEndTime);
            Assert.Null(singletonLock.ReleaseTime);
        }

        [Fact]
        public async Task AquireAsync_InvokesSingletonManager_WithExpectedValues()
        {
            CancellationToken cancellationToken = new CancellationToken();
            SingletonAttribute attribute = new SingletonAttribute();
            SingletonManager.SingletonLockHandle handle = new SingletonManager.SingletonLockHandle();

            Mock<SingletonManager> mockSingletonManager = new Mock<SingletonManager>(MockBehavior.Strict);
            mockSingletonManager.Setup(p => p.LockAsync(TestLockId, TestInstanceId, attribute, cancellationToken)).ReturnsAsync(handle);

            SingletonLock singletonLock = new SingletonLock(TestLockId, TestInstanceId, attribute, mockSingletonManager.Object);

            Assert.False(singletonLock.IsHeld);
            await singletonLock.AcquireAsync(cancellationToken);

            Assert.NotNull(singletonLock.AcquireStartTime);
            Assert.NotNull(singletonLock.AcquireEndTime);
            Assert.True(singletonLock.IsHeld);
        }

        [Fact]
        public async Task ReleaseAsync_InvokesSingletonManager_WithExpectedValues()
        {
            CancellationToken cancellationToken = new CancellationToken();
            SingletonAttribute attribute = new SingletonAttribute();
            SingletonManager.SingletonLockHandle handle = new SingletonManager.SingletonLockHandle();

            Mock<SingletonManager> mockSingletonManager = new Mock<SingletonManager>(MockBehavior.Strict);
            mockSingletonManager.Setup(p => p.LockAsync(TestLockId, TestInstanceId, attribute, cancellationToken)).ReturnsAsync(handle);
            mockSingletonManager.Setup(p => p.ReleaseLockAsync(handle, cancellationToken)).Returns(Task.FromResult(true));

            SingletonLock singletonLock = new SingletonLock(TestLockId, TestInstanceId, attribute, mockSingletonManager.Object);

            Assert.False(singletonLock.IsHeld);
            await singletonLock.AcquireAsync(cancellationToken);
            Assert.True(singletonLock.IsHeld);
            await singletonLock.ReleaseAsync(cancellationToken);
            Assert.False(singletonLock.IsHeld);

            Assert.NotNull(singletonLock.AcquireStartTime);
            Assert.NotNull(singletonLock.AcquireEndTime);
            Assert.NotNull(singletonLock.ReleaseTime);
        }

        [Fact]
        public async Task GetOwnerAsync_InvokesSingletonManager_WithExpectedValues()
        {
            CancellationToken cancellationToken = new CancellationToken();
            SingletonAttribute attribute = new SingletonAttribute();

            Mock<SingletonManager> mockSingletonManager = new Mock<SingletonManager>(MockBehavior.Strict);
            string lockOwner = "ownerid";
            mockSingletonManager.Setup(p => p.GetLockOwnerAsync(attribute, TestLockId, cancellationToken)).ReturnsAsync(lockOwner);

            SingletonLock singletonLock = new SingletonLock(TestLockId, TestInstanceId, attribute, mockSingletonManager.Object);

            await singletonLock.GetOwnerAsync(cancellationToken);
        }
    }
}
