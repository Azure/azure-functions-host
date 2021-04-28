// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Moq;
using Xunit;
using System.Net.Http;
using Moq.Protected;
using System.Net;
using Newtonsoft.Json;

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
            _environment.Setup(p => p.GetEnvironmentVariable("HTTP_LEADER_ENDPOINT")).Returns("http://test-endpoint");

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
        [InlineData(HttpStatusCode.BadRequest, "testlock", null)]
        [InlineData(HttpStatusCode.OK, "testlock", "testowner")]
        public async Task GetLockRequest_ReturnsCorrectLockHandle(HttpStatusCode status, string lockId, string ownerId)
        {

            Mock<IEnvironment> _environment = new Mock<IEnvironment>();
            _environment.Setup(p => p.GetEnvironmentVariable("HTTP_LEADER_ENDPOINT")).Returns("http://test-endpoint");

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
