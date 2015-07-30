// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Singleton
{
    public class SingletonValueProviderTests
    {
        private const string TestInstanceId = "testinstance";
        private readonly string _lockId;
        private readonly SingletonValueProvider _valueProvider;
        private readonly SingletonAttribute _attribute;
        private readonly MethodInfo _method;

        public SingletonValueProviderTests()
        {
            _attribute = new SingletonAttribute("TestScope");
            SingletonManager singletonManager = new SingletonManager();
            _method = GetType().GetMethod("TestJob", BindingFlags.Static | BindingFlags.Public);
            _lockId = SingletonManager.FormatLockId(_method, _attribute.Scope);
            _valueProvider = new SingletonValueProvider(_method, "TestScope", TestInstanceId, _attribute, singletonManager);
        }

        [Fact]
        public void Type_ReturnsExpectedValue()
        {
            Assert.Equal(typeof(SingletonLock), _valueProvider.Type);
        }

        [Fact]
        public void GetValue_ReturnsExpectedValue()
        {
            SingletonLock value = (SingletonLock)_valueProvider.GetValue();
            Assert.Equal(_lockId, value.Id);
            Assert.Equal(TestInstanceId, value.FunctionId);
        }

        [Fact]
        public void Watcher_ReturnsExpectedValue()
        {
            SingletonValueProvider.SingletonWatcher watcher = (SingletonValueProvider.SingletonWatcher)_valueProvider.Watcher;
            Assert.NotNull(watcher);
        }

        [Fact]
        public void ToInvokeString_ReturnsExpectedValue()
        {
            SingletonAttribute attribute = new SingletonAttribute();
            SingletonValueProvider localValueProvider = new SingletonValueProvider(_method, attribute.Scope, TestInstanceId, attribute, new SingletonManager());
            SingletonLock singletonLock = (SingletonLock)localValueProvider.GetValue();
            Assert.Equal("Scope: default", localValueProvider.ToInvokeString());

            attribute = new SingletonAttribute(@"{Region}\{Zone}");
            localValueProvider = new SingletonValueProvider(_method, @"West\3", TestInstanceId, attribute, new SingletonManager());
            singletonLock = (SingletonLock)localValueProvider.GetValue();
            Assert.Equal(@"Scope: West\3", localValueProvider.ToInvokeString());
        }

        [Fact]
        public void SingletonWatcher_GetStatus_ReturnsExpectedValue()
        {
            Mock<SingletonManager> mockSingletonManager = new Mock<SingletonManager>(MockBehavior.Strict);
            mockSingletonManager.Setup(p => p.GetLockOwnerAsync(_lockId, CancellationToken.None)).ReturnsAsync("someotherguy");
            SingletonValueProvider localValueProvider = new SingletonValueProvider(_method, _attribute.Scope, TestInstanceId, _attribute, mockSingletonManager.Object);
            SingletonLock localSingletonLock = (SingletonLock)localValueProvider.GetValue();

            DateTime startTime = DateTime.Now;
            DateTime endTime = startTime + TimeSpan.FromSeconds(2);
            DateTime releaseTime = endTime + TimeSpan.FromSeconds(1);

            // before lock is called
            SingletonValueProvider.SingletonWatcher watcher = (SingletonValueProvider.SingletonWatcher)localValueProvider.Watcher;
            SingletonParameterLog log = (SingletonParameterLog)watcher.GetStatus();
            Assert.Null(log.LockOwner);
            Assert.False(log.LockAcquired);
            Assert.Null(log.LockDuration);
            Assert.Null(log.TimeToAcquireLock);

            // in the process of locking
            localSingletonLock.AcquireStartTime = startTime;
            log = (SingletonParameterLog)watcher.GetStatus();
            Assert.Equal("someotherguy", log.LockOwner);
            Assert.False(log.LockAcquired);
            Assert.Null(log.LockDuration);
            Assert.NotNull(log.TimeToAcquireLock);

            // lock acquired but not released
            localSingletonLock.AcquireEndTime = endTime;
            log = (SingletonParameterLog)watcher.GetStatus();
            Assert.Null(log.LockOwner);
            Assert.True(log.LockAcquired);
            Assert.NotNull(log.LockDuration);
            Assert.Equal(endTime - startTime, log.TimeToAcquireLock);

            // lock released
            localSingletonLock.ReleaseTime = releaseTime;
            log = (SingletonParameterLog)watcher.GetStatus();
            Assert.Null(log.LockOwner);
            Assert.True(log.LockAcquired);
            Assert.Equal(releaseTime - endTime, log.LockDuration);
            Assert.Equal(endTime - startTime, log.TimeToAcquireLock);
        }

        public static void TestJob()
        {
        }
    }
}
