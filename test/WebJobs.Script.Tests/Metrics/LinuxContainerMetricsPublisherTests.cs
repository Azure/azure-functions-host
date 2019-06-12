// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Description;
using Microsoft.Azure.WebJobs.Host.Config;
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
        private readonly Dictionary<LogLevel, string> _events;
        private HttpClient _httpClient;

        public LinuxContainerMetricsPublisherTests()
        {
            _events = new Dictionary<LogLevel, string>();
            Action<LogLevel, string> writer = (loglevel, s) =>
            {
                _events[loglevel] = s;
            };
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
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.NodeIpAddress)).Returns(_testIpAddress);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName)).Returns(_testHostName);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteHomeStampName)).Returns(_testStampName);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteStampDeploymentId)).Returns(_testTenant);

            var websiteAuthEncryptionKey = TestHelpers.GenerateKeyBytes();
            var websiteAuthEncryptionStringKey = TestHelpers.GenerateKeyHexString(websiteAuthEncryptionKey);
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey, websiteAuthEncryptionStringKey);

            var standbyOptions = new TestOptionsMonitor<StandbyOptions>(new StandbyOptions { InStandbyMode = true });
            _metricsPublisher = new LinuxContainerMetricsPublisher(mockEnvironment.Object, standbyOptions, writer, _httpClient);
            Assert.Empty(_events);
        }

        private void ValidateRequest(HttpRequestMessage request)
        {
            Assert.Equal(request.Headers.GetValues(LinuxContainerMetricsPublisher.ContainerNameHeader).Single(), _containerName);
            Assert.Equal(request.Headers.GetValues(LinuxContainerMetricsPublisher.HostNameHeader).Single(), _testHostName);
            Assert.Equal(request.Headers.GetValues(LinuxContainerMetricsPublisher.StampNameHeader).Single(), _testStampName);
            Assert.NotEmpty(request.Headers.GetValues(LinuxContainerMetricsPublisher.SiteTokenHeader));

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
            else
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
            _metricsPublisher.InitializeRequestHeaders();
            _metricsPublisher.AddFunctionExecutionActivity(
                _testFunctionActivity.FunctionName,
                _testFunctionActivity.InvocationId,
                _testFunctionActivity.Concurrency,
                _testFunctionActivity.ExecutionStage,
                _testFunctionActivity.IsSucceeded,
                _testFunctionActivity.ExecutionTimeSpanInMs,
                _testFunctionActivity.EventTimeStamp);
            Assert.Matches("Added function activity", _events[LogLevel.Information]);
            _events.Clear();

            _metricsPublisher.OnFunctionMetricsPublishTimer(null);
            _metricsPublisher.OnFunctionMetricsPublishTimer(null);
            Assert.Empty(_events);
        }

        [Fact]
        public void PublishMemoryActivity_SendsRequestHeaders()
        {
            _metricsPublisher.InitializeRequestHeaders();
            DateTime currentTime = DateTime.UtcNow;
            _metricsPublisher.AddMemoryActivity(_testMemoryActivity.EventTimeStamp, _testMemoryActivity.CommitSizeInBytes);
            Assert.Empty(_events);

            _metricsPublisher.OnFunctionMetricsPublishTimer(null);
            _metricsPublisher.OnFunctionMetricsPublishTimer(null);
            Assert.Empty(_events);
        }

        [Fact]
        public void PublishMemoryActivity_FailsWithoutRequestHeaders()
        {
            DateTime currentTime = DateTime.UtcNow;
            _metricsPublisher.AddMemoryActivity(_testMemoryActivity.EventTimeStamp, _testMemoryActivity.CommitSizeInBytes);
            Assert.Empty(_events);

            _metricsPublisher.OnFunctionMetricsPublishTimer(null);
            _metricsPublisher.OnFunctionMetricsPublishTimer(null);
            Assert.Single(_events);
            Assert.Matches("Error", _events[LogLevel.Warning]);
        }

        [Fact]
        public void PublishFunctionActivity_FailsWithoutRequestHeaders()
        {
            DateTime currentTime = DateTime.UtcNow;
            _metricsPublisher.AddFunctionExecutionActivity(
            _testFunctionActivity.FunctionName,
            _testFunctionActivity.InvocationId,
            _testFunctionActivity.Concurrency,
            _testFunctionActivity.ExecutionStage,
            _testFunctionActivity.IsSucceeded,
            _testFunctionActivity.ExecutionTimeSpanInMs,
            _testFunctionActivity.EventTimeStamp);

            Assert.Single(_events);
            Assert.Matches("Added function activity", _events[LogLevel.Information]);
            _events.Clear();

            _metricsPublisher.OnFunctionMetricsPublishTimer(null);
            _metricsPublisher.OnFunctionMetricsPublishTimer(null);
            Assert.Single(_events);
            Assert.Matches("Error", _events[LogLevel.Warning]);
        }
    }
}
