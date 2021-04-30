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

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class KubernetesDistributedLockManagerTests
    {
        [Theory]
        [InlineData(HttpStatusCode.BadRequest, "testlock", "testowner", null, null)]
        [InlineData(HttpStatusCode.OK, "testlock", "testowner", "testlock", "testowner")]
        public async Task AcquireLockRequest_ReturnsCorrectLockHandle(HttpStatusCode status, string lockId, string ownerId, string expectedLockId, string expectedOwnerId)
        {

            Mock<IEnvironment> _environment = new Mock<IEnvironment>();
            _environment.Setup(p => p.GetEnvironmentVariable(HttpLeaderEndpoint)).Returns("http://test-endpoint");

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                       ItExpr.IsAny<HttpRequestMessage>(),
                       ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
                       {
                           StatusCode = status
                       });

            var httpClient = new HttpClient(handlerMock.Object);

            var lockClient = new KubernetesClient(_environment.Object, httpClient);
            var lockHandle = await lockClient.TryAcquireLock(lockId, ownerId, TimeSpan.FromSeconds(5), new CancellationToken());
            Assert.Equal(expectedLockId, lockHandle.LockId);
            Assert.Equal(expectedOwnerId, lockHandle.Owner);
        }

        [Theory]
        [InlineData("", "")]
        [InlineData(null, "")]
        public async Task AcquireLockRequestThrowsOnInvalidInput(string lockId, string ownerId)
        {

            Mock<IEnvironment> _environment = new Mock<IEnvironment>();
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            var httpClient = new HttpClient(handlerMock.Object);

            var lockClient = new KubernetesClient(_environment.Object, httpClient);
            await Assert.ThrowsAsync<ArgumentNullException>(() => lockClient.TryAcquireLock(lockId, ownerId, TimeSpan.FromSeconds(5), new CancellationToken()));
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
                       ItExpr.IsAny<HttpRequestMessage>(),
                       ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
                       {
                           StatusCode = status,
                           Content = new StringContent(JsonConvert.SerializeObject(expectedLockHandle))
                       });

            var httpClient = new HttpClient(handlerMock.Object);

            var lockClient = new KubernetesClient(_environment.Object, httpClient);

            if (status != HttpStatusCode.OK)
            {
                await Assert.ThrowsAsync<HttpRequestException>(() => lockClient.GetLock(lockId));
            }
            else
            {
                var actualLockHandle = await lockClient.GetLock(lockId);
                Assert.Equal(expectedLockHandle.Owner, actualLockHandle.Owner);
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
            var lockClient = new KubernetesClient(_environment.Object, httpClient);

            await Assert.ThrowsAsync<ArgumentNullException>(() => lockClient.GetLock(lockId));    
        }

        [Theory]
        [InlineData("", "")]
        [InlineData(null, "")]
        public async Task ReleaseLockRequest_ThrowsOnInvalidInput(string lockId, string ownerId)
        {

            Mock<IEnvironment> _environment = new Mock<IEnvironment>();
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            var httpClient = new HttpClient(handlerMock.Object);
            var lockClient = new KubernetesClient(_environment.Object, httpClient);

            await Assert.ThrowsAsync<ArgumentNullException>(() => lockClient.ReleaseLock(lockId, ownerId));
        }


        [Theory]
        [InlineData("http://test-endpoint", "/testpath", "http://test-endpoint/lock/testpath")]
        [InlineData("http://test-endpoint", "", "http://test-endpoint/lock")]
        public void GetRequestUri_UsesHttpLeaderEndpoint(string leaderEndpoint, string pathAndQueryString, string expected)
        {
            Mock<IEnvironment> _environment = new Mock<IEnvironment>();
            _environment.Setup(p => p.GetEnvironmentVariable("HTTP_LEADER_ENDPOINT")).Returns(leaderEndpoint);

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            var httpClient = new HttpClient(handlerMock.Object);

            var lockClient = new KubernetesClient(_environment.Object, httpClient);

            var actual = lockClient.GetRequestUri(pathAndQueryString);
            Assert.Equal(expected, actual.AbsoluteUri);
        }
    }
}
