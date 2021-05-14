// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;
using System.Net.Http;
using Moq.Protected;
using System.Net;
using Newtonsoft.Json;
using static Microsoft.Azure.WebJobs.Script.EnvironmentSettingNames;
using Microsoft.Azure.WebJobs.Script.Config;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class KubernetesDistributedLockManagerTests
    {
        private const string TestHttpLeaderEndpoint = "http://test-endpoint";

        [Theory]
        [InlineData(HttpStatusCode.BadRequest, "testlock", "testowner", null, null)]
        [InlineData(HttpStatusCode.OK, "testlock", "testowner", "testlock", "testowner")]
        public async Task AcquireLockRequest_ReturnsCorrectLockHandle(HttpStatusCode status, string lockId, string ownerId, string expectedLockId, string expectedOwnerId)
        {
            Mock<IEnvironment> _environment = new Mock<IEnvironment>();
            _environment.Setup(p => p.GetEnvironmentVariable(HttpLeaderEndpoint)).Returns(TestHttpLeaderEndpoint);

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                       ItExpr.Is<HttpRequestMessage>(request =>
                       request.Method == HttpMethod.Post &&
                       request.RequestUri.AbsoluteUri.Equals(
                           $"{TestHttpLeaderEndpoint}/lock/acquire?name={lockId}&owner={ownerId}&duration=5&renewDeadline=10")),
                       ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
                       {
                           StatusCode = status
                       });

            var httpClient = new HttpClient(handlerMock.Object);

            var lockClient = new KubernetesClient(_environment.Object, httpClient);
            var lockManager = new KubernetesDistributedLockManager(lockClient, ScriptSettingsManager.Instance);
            var lockHandle = await lockManager.TryLockAsync("", lockId, ownerId, "",TimeSpan.FromSeconds(5), new CancellationToken());

            if (status == HttpStatusCode.OK)
            {
                Assert.IsType<KubernetesLockHandle> (lockHandle);

                var kubernetesLockHandleResult = (KubernetesLockHandle)lockHandle;
                Assert.Equal(expectedLockId, kubernetesLockHandleResult.LockId);
                Assert.Equal(expectedOwnerId, kubernetesLockHandleResult.Owner);
                Assert.Equal("5", kubernetesLockHandleResult.LockPeriod);
            }
            else
            {
                Assert.Null(lockHandle);
            }
        }

        [Fact]
        public async Task AcquireLockRequest_UsesWebsiteinstanceIdIfOwnerMissing()
        {
            Mock<IEnvironment> _environment = new Mock<IEnvironment>();
            _environment.Setup(p => p.GetEnvironmentVariable(HttpLeaderEndpoint)).Returns(TestHttpLeaderEndpoint);

            string lockId = "test-lock";
            string instanceId = ScriptSettingsManager.Instance.AzureWebsiteInstanceId;

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                       ItExpr.Is<HttpRequestMessage>(request =>
                       request.Method == HttpMethod.Post &&
                       request.RequestUri.AbsoluteUri.Equals(
                           $"{TestHttpLeaderEndpoint}/lock/acquire?name={lockId}&owner={instanceId}&duration=5&renewDeadline=10")),
                       ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
                       {
                           StatusCode = HttpStatusCode.OK
                       });

            var httpClient = new HttpClient(handlerMock.Object);

            var lockClient = new KubernetesClient(_environment.Object, httpClient);
            var lockManager = new KubernetesDistributedLockManager(lockClient, ScriptSettingsManager.Instance);
            var lockHandle = await lockManager.TryLockAsync("", lockId, "", "", TimeSpan.FromSeconds(5), new CancellationToken());

            Assert.Equal(lockId, lockHandle.LockId);
            Assert.Equal(instanceId, ((KubernetesLockHandle)lockHandle).Owner);
        }

        [Theory]
        [InlineData("", "")]
        [InlineData(null, "")]
        [InlineData(null, null)]
        [InlineData("", null)]
        [InlineData("", "testOwner")]
        public async Task AcquireLockRequestThrowsOnInvalidInput(string lockId, string ownerId)
        {
            Mock<IEnvironment> _environment = new Mock<IEnvironment>();
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            var httpClient = new HttpClient(handlerMock.Object);

            var lockClient = new KubernetesClient(_environment.Object, httpClient);
            var lockManager = new KubernetesDistributedLockManager(lockClient, ScriptSettingsManager.Instance);
            await Assert.ThrowsAsync<ArgumentNullException>(() => lockManager.TryLockAsync
            ("", lockId, ownerId, "", TimeSpan.FromSeconds(5), new CancellationToken()));
        }

        [Theory]
        [InlineData(HttpStatusCode.BadRequest, "testlock", null)]
        [InlineData(HttpStatusCode.OK, "testlock", "testowner")]
        public async Task GetLockRequest_ReturnsCorrectLockHandle(HttpStatusCode status, string lockId, string ownerId)
        {

            Mock<IEnvironment> _environment = new Mock<IEnvironment>();
            _environment.Setup(p => p.GetEnvironmentVariable(HttpLeaderEndpoint)).Returns("http://test-endpoint");

            var expectedLockHandle = new KubernetesLockHandle() { Owner = ownerId };

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                       ItExpr.Is<HttpRequestMessage>(request =>
                              request.Method == HttpMethod.Get &&
                              request.RequestUri.AbsoluteUri.Equals($"{TestHttpLeaderEndpoint}/lock?name={lockId}")),
                       ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
                       {
                           StatusCode = status,
                           Content = new StringContent(JsonConvert.SerializeObject(expectedLockHandle))
                       });

            var httpClient = new HttpClient(handlerMock.Object);

            var lockClient = new KubernetesClient(_environment.Object, httpClient);
            var lockManager = new KubernetesDistributedLockManager(lockClient, ScriptSettingsManager.Instance);

            if (status != HttpStatusCode.OK)
            {
                await Assert.ThrowsAsync<HttpRequestException>(() => lockManager.GetLockOwnerAsync(
                    "", lockId, new CancellationToken()));
            }
            else
            {
                var actualOwner = await lockManager.GetLockOwnerAsync("", lockId, new CancellationToken());
                Assert.Equal(expectedLockHandle.Owner, actualOwner);
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task GetLockRequest_ThrowsOnInvalidInput(string lockId)
        {
            Mock<IEnvironment> _environment = new Mock<IEnvironment>();
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            var httpClient = new HttpClient(handlerMock.Object);
            var lockManager = new KubernetesDistributedLockManager(
                new KubernetesClient(_environment.Object, httpClient), ScriptSettingsManager.Instance);

            await Assert.ThrowsAsync<ArgumentNullException>(() => lockManager.GetLockOwnerAsync(
                "", lockId, new CancellationToken()));    
        }

        [Theory]
        [InlineData("", "")]
        [InlineData(null, "")]
        public async Task ReleaseLockRequest_ThrowsOnInvalidInput(string lockId, string ownerId)
        {
            Mock<IEnvironment> _environment = new Mock<IEnvironment>();
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            var httpClient = new HttpClient(handlerMock.Object);
            var lockManager = new KubernetesDistributedLockManager(
                new KubernetesClient(_environment.Object, httpClient), ScriptSettingsManager.Instance);

            var inputLock = new KubernetesLockHandle() { LockId = lockId, Owner = ownerId };

            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                   lockManager.ReleaseLockAsync(inputLock, new CancellationToken()));
        }


        [Theory]
        [InlineData("http://test-endpoint", "/testpath", "http://test-endpoint/lock/testpath")]
        [InlineData("http://test-endpoint", "", "http://test-endpoint/lock")]
        public void GetRequestUri_UsesHttpLeaderEndpoint(string leaderEndpoint, string pathAndQueryString, string expected)
        {
            Mock<IEnvironment> _environment = new Mock<IEnvironment>();
            _environment.Setup(p => p.GetEnvironmentVariable(HttpLeaderEndpoint)).Returns(leaderEndpoint);

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            var httpClient = new HttpClient(handlerMock.Object);

            var lockClient = new KubernetesClient(_environment.Object, httpClient);

            var actual = lockClient.GetRequestUri(pathAndQueryString);
            Assert.Equal(expected, actual.AbsoluteUri);
        }
    }
}
