// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Metrics;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Moq.Protected;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Metrics
{
    public class LinuxContainerMetricsPublisherTests
    {
        private const string _containerName = "test-container";
        private const string _testIpAddress = "test-ip";
        private const string _testHostName = "test-host";
        private const string _testStampName = "test-stamp";
        private const string _testTenant = "test-tenant";

        private readonly FunctionActivity _testFunctionActivity = new FunctionActivity
        {
            FunctionName = string.Empty,
            InvocationId = string.Empty,
            Concurrency = 1,
            ExecutionStage = string.Empty,
            IsSucceeded = true,
            ExecutionTimeSpanInMs = 1,
            EventTimeStamp = DateTime.UtcNow,
        };

        private readonly MemoryActivity _testMemoryActivity = new MemoryActivity
        {
            CommitSizeInBytes = 1234,
            EventTimeStamp = DateTime.UtcNow,
            Tenant = _testTenant
        };

        private readonly LinuxContainerMetricsPublisher _metricsPublisher;
        private readonly TestLoggerProvider _testLoggerProvider;
        private HttpClient _httpClient;

        public LinuxContainerMetricsPublisherTests()
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((request, token) => ValidateRequest(request))
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK
                });

            _httpClient = new HttpClient(handlerMock.Object);

            var mockEnvironment = new Mock<IEnvironment>(MockBehavior.Strict);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.ContainerName)).Returns(_containerName);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.LinuxNodeIpAddress)).Returns(_testIpAddress);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName)).Returns(_testHostName);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteHomeStampName)).Returns(_testStampName);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteStampDeploymentId)).Returns(_testTenant);

            var websiteAuthEncryptionKey = TestHelpers.GenerateKeyBytes();
            var websiteAuthEncryptionStringKey = TestHelpers.GenerateKeyHexString(websiteAuthEncryptionKey);
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey, websiteAuthEncryptionStringKey);

            _testLoggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_testLoggerProvider);

            ILogger<LinuxContainerMetricsPublisher> logger = loggerFactory.CreateLogger<LinuxContainerMetricsPublisher>();
            var hostNameProvider = new HostNameProvider(mockEnvironment.Object, new Mock<ILogger<HostNameProvider>>().Object);
            var standbyOptions = new TestOptionsMonitor<StandbyOptions>(new StandbyOptions { InStandbyMode = true });
            _metricsPublisher = new LinuxContainerMetricsPublisher(mockEnvironment.Object, standbyOptions, logger, _httpClient, hostNameProvider);
            _testLoggerProvider.ClearAllLogMessages();
        }

        private void ValidateRequest(HttpRequestMessage request)
        {
            Assert.Equal(request.Headers.GetValues(LinuxContainerMetricsPublisher.ContainerNameHeader).Single(), _containerName);
            Assert.Equal(request.Headers.GetValues(LinuxContainerMetricsPublisher.HostNameHeader).Single(), _testHostName);
            Assert.Equal(request.Headers.GetValues(LinuxContainerMetricsPublisher.StampNameHeader).Single(), _testStampName);
            Assert.NotEmpty(request.Headers.GetValues(ScriptConstants.SiteTokenHeaderName));

            Assert.Equal(request.RequestUri.Host, _testIpAddress);

            string requestPath = request.RequestUri.AbsolutePath;

            if (requestPath.Contains(LinuxContainerMetricsPublisher.PublishFunctionActivityPath))
            {
                ObjectContent requestContent = (ObjectContent)request.Content;
                Assert.Equal(requestContent.Value.GetType(), typeof(FunctionActivity[]));

                IEnumerable<FunctionActivity> activitesPayload = (FunctionActivity[])requestContent.Value;
                Assert.Equal(activitesPayload.Single().FunctionName, _testFunctionActivity.FunctionName);
                Assert.Equal(activitesPayload.Single().InvocationId, _testFunctionActivity.InvocationId);
                Assert.Equal(activitesPayload.Single().Concurrency, _testFunctionActivity.Concurrency);
                Assert.Equal(activitesPayload.Single().ExecutionStage, _testFunctionActivity.ExecutionStage);
                Assert.Equal(activitesPayload.Single().IsSucceeded, _testFunctionActivity.IsSucceeded);
                Assert.Equal(activitesPayload.Single().ExecutionTimeSpanInMs, _testFunctionActivity.ExecutionTimeSpanInMs);
                Assert.Equal(activitesPayload.Single().Tenant, _testTenant);
                Assert.Equal(activitesPayload.Single().EventTimeStamp, _testFunctionActivity.EventTimeStamp);
            }
            else if (requestPath.Contains(LinuxContainerMetricsPublisher.PublishMemoryActivityPath))
            {
                ObjectContent requestContent = (ObjectContent)request.Content;
                Assert.Equal(requestContent.Value.GetType(), typeof(MemoryActivity[]));

                IEnumerable<MemoryActivity> activitesPayload = (MemoryActivity[])requestContent.Value;
                Assert.Equal(activitesPayload.Single().CommitSizeInBytes, _testMemoryActivity.CommitSizeInBytes);
                Assert.Equal(activitesPayload.Single().EventTimeStamp, _testMemoryActivity.EventTimeStamp);
                Assert.Equal(activitesPayload.Single().Tenant, _testTenant);
            }
        }

        [Fact]
        public void PublishFunctionActivity_SendsRequestHeaders()
        {
            _metricsPublisher.Initialize();
            _metricsPublisher.AddFunctionExecutionActivity(
                _testFunctionActivity.FunctionName,
                _testFunctionActivity.InvocationId,
                _testFunctionActivity.Concurrency,
                _testFunctionActivity.ExecutionStage,
                _testFunctionActivity.IsSucceeded,
                _testFunctionActivity.ExecutionTimeSpanInMs,
                _testFunctionActivity.EventTimeStamp);

            Assert.Matches("Added function activity", _testLoggerProvider.GetAllLogMessages().Single().FormattedMessage);
            Assert.Equal(LogLevel.Debug, _testLoggerProvider.GetAllLogMessages().Single().Level);

            _testLoggerProvider.ClearAllLogMessages();

            _metricsPublisher.OnFunctionMetricsPublishTimer(null);
            _metricsPublisher.OnFunctionMetricsPublishTimer(null);
            Assert.Empty(_testLoggerProvider.GetAllLogMessages());
        }

        [Fact]
        public void PublishMemoryActivity_SendsRequestHeaders()
        {
            _metricsPublisher.Initialize();
            DateTime currentTime = DateTime.UtcNow;
            _metricsPublisher.AddMemoryActivity(_testMemoryActivity.EventTimeStamp, _testMemoryActivity.CommitSizeInBytes);
            Assert.Empty(_testLoggerProvider.GetLog());

            _metricsPublisher.OnFunctionMetricsPublishTimer(null);
            _metricsPublisher.OnFunctionMetricsPublishTimer(null);
            Assert.Empty(_testLoggerProvider.GetLog());
        }

        [Fact]
        public void SendRequest_FailsWithNullQueue()
        {
            ConcurrentQueue<string> testQueue = null;
            Assert.Throws<NullReferenceException>(() => _metricsPublisher.SendRequest(testQueue, "testPath").GetAwaiter().GetResult());
        }

        [Fact]
        public void SendRequest_SucceedsWithEmptyQueue()
        {
            ConcurrentQueue<string> testQueue = new ConcurrentQueue<string>();
            _metricsPublisher.Initialize();
            _metricsPublisher.SendRequest(testQueue, "testPath").GetAwaiter().GetResult();
        }
    }
}
