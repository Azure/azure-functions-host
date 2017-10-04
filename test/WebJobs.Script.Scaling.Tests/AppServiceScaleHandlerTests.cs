// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Scaling.Tests
{
    [Collection("Azure Test Collection")]
    public class AppServiceScaleHandlerTests
    {
        [Theory, MemberData("PingWorkerData")]
        public async Task PingWorkerTests(HttpStatusCode statusCode, string reasonPhrase, bool expected, HttpRequestException expectedException)
        {
            var activityId = Guid.NewGuid().ToString();
            var mockHttpMessageHandler = new MockHttpMessageHandler(statusCode, reasonPhrase);

            AppServiceSettings.SiteName = Environment.MachineName.ToLower();
            AppServiceSettings.HostName = Environment.MachineName.ToLower() + ".azurewebsites.net";
            AppServiceSettings.WorkerName = "127.0.0.1";
            AppServiceSettings.HomeStampName = "waws-prod-home-stamp";
            AppServiceSettings.CurrentStampName = "waws-prod-slave-stamp";
            AppServiceSettings.RuntimeEncryptionKey = ScaleUtilsTests.GenerateEncryptionKey();
            try
            {
                AppServiceScaleHandler.HttpMessageHandler = mockHttpMessageHandler;

                var worker = new AppServiceWorkerInfo
                {
                    StampName = AppServiceSettings.CurrentStampName,
                    WorkerName = AppServiceSettings.WorkerName
                };

                var handler = AppServiceScaleHandler.Instance;

                // test
                if (expectedException == null)
                {
                    var actual = await handler.PingWorker(activityId, worker);

                    Assert.Equal(expected, actual);
                }
                else
                {
                    var exception = await Assert.ThrowsAsync<HttpRequestException>(async () => await handler.PingWorker(activityId, worker));

                    Assert.Contains(expectedException.Message, exception.Message);
                }

                Assert.Equal(activityId, mockHttpMessageHandler.ActivityId);
                Assert.Equal(AppServiceSettings.HostName, mockHttpMessageHandler.Host);
                Assert.Equal(worker.StampName + ".cloudapp.net", mockHttpMessageHandler.Uri.Host);
                Assert.Equal("https", mockHttpMessageHandler.Uri.Scheme);
                Assert.Equal("/operations/keepalive/" + AppServiceSettings.SiteName + "/" + worker.WorkerName, mockHttpMessageHandler.Uri.AbsolutePath);
            }
            finally
            {
                ResetEnvironment();
            }
        }

        public static IEnumerable<object[]> PingWorkerData
        {
            get
            {
                yield return new object[] { HttpStatusCode.NoContent, null, true, null };
                yield return new object[] { HttpStatusCode.NotFound, "Worker Not Found", false, null };
                yield return new object[] { HttpStatusCode.InternalServerError, "Worker Not Found", false, new HttpRequestException(" 500 ") };
                yield return new object[] { HttpStatusCode.NotFound, null, false, new HttpRequestException(" 404 ") };
            }
        }

        [Theory, MemberData("AddWorkerData")]
        public async Task AddWorkerTests(HttpStatusCode statusCode, string reasonPhrase, string expected, HttpRequestException expectedException)
        {
            var activityId = Guid.NewGuid().ToString();
            var mockHttpMessageHandler = new MockHttpMessageHandler(statusCode, reasonPhrase);

            AppServiceSettings.SiteName = Environment.MachineName.ToLower();
            AppServiceSettings.HostName = Environment.MachineName.ToLower() + ".azurewebsites.net";
            AppServiceSettings.WorkerName = "127.0.0.1";
            AppServiceSettings.HomeStampName = "waws-prod-home-stamp";
            AppServiceSettings.CurrentStampName = "waws-prod-slave-stamp";
            AppServiceSettings.RuntimeEncryptionKey = ScaleUtilsTests.GenerateEncryptionKey();
            try
            {
                AppServiceScaleHandler.HttpMessageHandler = mockHttpMessageHandler;

                var handler = AppServiceScaleHandler.Instance;
                var stampNames = new[] { "waws-prod-slave-stamp" };

                // test
                if (expectedException == null)
                {
                    var actual = await handler.AddWorker(activityId, stampNames, 1);

                    Assert.Equal(expected, actual);
                }
                else
                {
                    var exception = await Assert.ThrowsAsync<HttpRequestException>(async () => await handler.AddWorker(activityId, stampNames, 1));

                    Assert.Contains(expectedException.Message, exception.Message);
                }

                Assert.Equal(activityId, mockHttpMessageHandler.ActivityId);
                Assert.Equal(AppServiceSettings.HostName, mockHttpMessageHandler.Host);
                if (!string.IsNullOrEmpty(expected) || expectedException != null)
                {
                    Assert.Equal(AppServiceSettings.HomeStampName + ".cloudapp.net", mockHttpMessageHandler.Uri.Host);
                }
                else
                {
                    Assert.Equal(AppServiceSettings.CurrentStampName + ".cloudapp.net", mockHttpMessageHandler.Uri.Host);
                }
                Assert.Equal("https", mockHttpMessageHandler.Uri.Scheme);
                Assert.Equal("/operations/addworker/" + AppServiceSettings.SiteName, mockHttpMessageHandler.Uri.AbsolutePath);
            }
            finally
            {
                ResetEnvironment();
            }
        }

        public static IEnumerable<object[]> AddWorkerData
        {
            get
            {
                yield return new object[] { HttpStatusCode.NoContent, null, "waws-prod-home-stamp", null };
                yield return new object[] { HttpStatusCode.NotFound, "No Capacity", null, null };
                yield return new object[] { HttpStatusCode.InternalServerError, null, null, new HttpRequestException(" 500 ") };
            }
        }

        [Theory, MemberData("RemoveWorkerData")]
        public async Task RemoveWorkerTests(HttpStatusCode statusCode, Exception expectedException)
        {
            var activityId = Guid.NewGuid().ToString();
            var mockHttpMessageHandler = new MockHttpMessageHandler(statusCode);

            AppServiceSettings.SiteName = Environment.MachineName.ToLower();
            AppServiceSettings.HostName = Environment.MachineName.ToLower() + ".azurewebsites.net";
            AppServiceSettings.WorkerName = "127.0.0.1";
            AppServiceSettings.HomeStampName = "waws-prod-home-stamp";
            AppServiceSettings.CurrentStampName = "waws-prod-slave-stamp";
            AppServiceSettings.RuntimeEncryptionKey = ScaleUtilsTests.GenerateEncryptionKey();
            try
            {
                AppServiceScaleHandler.HttpMessageHandler = mockHttpMessageHandler;

                var handler = AppServiceScaleHandler.Instance;

                var worker = new AppServiceWorkerInfo
                {
                    StampName = AppServiceSettings.CurrentStampName,
                    WorkerName = AppServiceSettings.WorkerName
                };

                // test
                if (expectedException == null)
                {
                    await handler.RemoveWorker(activityId, worker);
                }
                else
                {
                    var exception = await Assert.ThrowsAsync<HttpRequestException>(async () => await handler.RemoveWorker(activityId, worker));

                    Assert.Contains(expectedException.Message, exception.Message);
                }

                Assert.Equal(activityId, mockHttpMessageHandler.ActivityId);
                Assert.Equal(AppServiceSettings.HostName, mockHttpMessageHandler.Host);
                Assert.Equal(worker.StampName + ".cloudapp.net", mockHttpMessageHandler.Uri.Host);
                Assert.Equal("https", mockHttpMessageHandler.Uri.Scheme);
                Assert.Equal("/operations/removeworker/" + AppServiceSettings.SiteName + "/" + worker.WorkerName, mockHttpMessageHandler.Uri.AbsolutePath);
            }
            finally
            {
                ResetEnvironment();
            }
        }

        public static IEnumerable<object[]> RemoveWorkerData
        {
            get
            {
                yield return new object[] { HttpStatusCode.NoContent, null };
                yield return new object[] { HttpStatusCode.ServiceUnavailable, new HttpRequestException(" 503 ") };
            }
        }

        private void ResetEnvironment()
        {
            AppServiceScaleHandler.HttpMessageHandler = null;
            AppServiceSettings.StorageConnectionString = null;
            AppServiceSettings.SiteName = null;
            AppServiceSettings.WorkerName = null;
            AppServiceSettings.HomeStampName = null;
            AppServiceSettings.CurrentStampName = null;
            AppServiceSettings.RuntimeEncryptionKey = null;
        }
    }
}