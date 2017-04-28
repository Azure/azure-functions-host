// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Scaling
{
    public sealed class AppServiceScaleHandler : IScaleHandler
    {
        public const int HttpTimeoutSeconds = 60;

        public const string WorkerNotFound = "Worker Not Found";
        [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly")]
        public const string SiteUnavailableFromMiniArr = "Site Unavailable from Mini-ARR";
        public const string NoCapacity = "No Capacity";
        public const string ScaleNotAllowed = "Scale Not Allowed";

        [SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes", Justification = "By design")]
        public static readonly AppServiceScaleHandler Instance = new AppServiceScaleHandler();

        private static readonly TimeSpan TokenValidity = TimeSpan.FromHours(1);

        private static readonly Lazy<ProductInfoHeaderValue> UserAgentHeader = new Lazy<ProductInfoHeaderValue>(() =>
        {
            var assembly = typeof(AppServiceScaleHandler).Assembly;
            var fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            return new ProductInfoHeaderValue("ScaleManager", fvi.ProductVersion);
        });

        private static string _token;
        private static DateTime _tokenExpiredUtc = DateTime.MinValue;

        private static HttpClient _httpClient;
        private static HttpMessageHandler _httpMessageHandler;

        private AppServiceScaleHandler()
        {
        }

        /// <summary>
        /// Gets or sets message handler for mock testing and intentionally not thread-safe.
        /// </summary>
        public static HttpMessageHandler HttpMessageHandler
        {
            get
            {
                return _httpMessageHandler;
            }

            set
            {
                _httpClient?.Dispose();
                _httpClient = null;
                _httpMessageHandler = value;
            }
        }

        /// <summary>
        /// This requests FE to add a worker.
        /// - FE returns success if new worker is assigned.
        /// - FE returns 404 No Capacity if no capacity and we will try on other stamps.
        /// - Other than that throws.
        /// </summary>
        public async Task<string> AddWorker(string activityId, IEnumerable<string> stampNames, int workers)
        {
            var list = stampNames.ToList();

            // always scale home stamp first
            list.Remove(AppServiceSettings.HomeStampName);
            list.Insert(0, AppServiceSettings.HomeStampName);

            foreach (var stampName in list)
            {
                var stampHostName = GetStampHostName(stampName);

                var worker = new AppServiceWorkerInfo
                {
                    PartitionKey = AppServiceSettings.SiteName,
                    StampName = stampName,
                    WorkerName = string.Empty
                };

                var details = string.Format("Add worker request from {0}:{1}", AppServiceSettings.CurrentStampName, AppServiceSettings.WorkerName);
                var pathAndQuery = string.Format("https://{0}/operations/addworker/{1}?token={2}&workers={3}",
                    stampHostName,
                    AppServiceSettings.SiteName,
                    GetToken(),
                    workers);

                using (var response = await SendAsync(activityId, HttpMethod.Post, pathAndQuery, worker, details))
                {
                    try
                    {
                        response.EnsureSuccessStatusCode();
                        return stampName;
                    }
                    catch (HttpRequestException)
                    {
                        // No capicity on this stamp
                        if (response.StatusCode == HttpStatusCode.NotFound &&
                            string.Equals(response.ReasonPhrase, NoCapacity, StringComparison.OrdinalIgnoreCase))
                        {
                            // try other stamps
                            continue;
                        }

                        throw;
                    }
                }
            }

            // already try all stamps and still no capacity
            return null;
        }

        /// <summary>
        /// This pings a specific worker.  The outcome could be ..
        /// - FE may return 404 Worker Not Found.  This routine will return false and
        /// worker will be removed from the table (done by manager or worker itself).
        /// - Worker returns success (2xx).  Worker itself will update its status on the table.
        /// - Other than that throws.
        /// </summary>
        public async Task<bool> PingWorker(string activityId, IWorkerInfo worker)
        {
            var stampHostName = GetStampHostName(worker.StampName);
            var details = string.Format("Ping worker request from {0}:{1}", AppServiceSettings.CurrentStampName, AppServiceSettings.WorkerName);
            var pathAndQuery = string.Format("https://{0}/operations/keepalive/{1}/{2}?token={3}",
                stampHostName,
                AppServiceSettings.SiteName,
                worker.WorkerName,
                GetToken());

            using (var response = await SendAsync(activityId, HttpMethod.Get, pathAndQuery, worker, details))
            {
                try
                {
                    response.EnsureSuccessStatusCode();
                    return true;
                }
                catch (HttpRequestException)
                {
                    // Worker is not valid for the stamp
                    if (response.StatusCode == HttpStatusCode.NotFound &&
                        string.Equals(response.ReasonPhrase, WorkerNotFound, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    // Worker is not valid for the stamp
                    if (response.StatusCode == HttpStatusCode.ServiceUnavailable &&
                        string.Equals(response.ReasonPhrase, SiteUnavailableFromMiniArr, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    throw;
                }
            }
        }

        /// <summary>
        /// This requests FE to remove this specific worker.
        /// - FE should return success regardless if worker belongs.
        /// - Any unexpected error will throw.
        /// </summary>
        public async Task RemoveWorker(string activityId, IWorkerInfo worker)
        {
            var stampHostName = GetStampHostName(worker.StampName);
            var details = string.Format("Remove worker request from {0}:{1}", AppServiceSettings.CurrentStampName, AppServiceSettings.WorkerName);
            var pathAndQuery = string.Format("https://{0}/operations/removeworker/{1}/{2}?token={3}",
                stampHostName,
                AppServiceSettings.SiteName,
                worker.WorkerName,
                GetToken());

            using (var response = await SendAsync(activityId, HttpMethod.Delete, pathAndQuery, worker, details))
            {
                response.EnsureSuccessStatusCode();
            }
        }

        public static string GetStampHostName(string stampName)
        {
            return string.Format("{0}.cloudapp.net", stampName);
        }

        public static string GetToken()
        {
            if (string.IsNullOrEmpty(_token) || _tokenExpiredUtc < DateTime.UtcNow)
            {
                var expiredUtc = DateTime.UtcNow.Add(TokenValidity);
                var token = ScaleUtils.GetToken(expiredUtc);
                _token = WebUtility.UrlEncode(token);
                _tokenExpiredUtc = expiredUtc;
            }

            return _token;
        }

        private static async Task<HttpResponseMessage> SendAsync(string activityId, HttpMethod method, string pathAndQuery, IWorkerInfo worker, string details)
        {
            var client = GetHttpClient();
            var request = new HttpRequestMessage(method, pathAndQuery);
            request.Properties[HttpTraceProperty.Key] = new HttpTraceProperty
            {
                Worker = worker,
                ActivityId = activityId,
                Details = details
            };

            return await client.SendAsync(request);
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "By design")]
        private static HttpClient GetHttpClient()
        {
            if (_httpClient == null)
            {
                var handler = _httpMessageHandler;
                if (handler == null)
                {
                    var webHandler = new WebRequestHandler();
                    if (!AppServiceSettings.ValidateCertificates.Value)
                    {
                        webHandler.ServerCertificateValidationCallback = ServerCertificateValidation;
                    }

                    handler = webHandler;
                }

                var client = new HttpClient(new HttpLoggingHandler(handler));
                client.Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds);
                client.DefaultRequestHeaders.Host = AppServiceSettings.HostName;
                client.DefaultRequestHeaders.UserAgent.Add(UserAgentHeader.Value);

                _httpClient = client;
            }

            return _httpClient;
        }

        private static bool ServerCertificateValidation(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return (sslPolicyErrors & SslPolicyErrors.RemoteCertificateNotAvailable) == 0;
        }

        private class HttpLoggingHandler : DelegatingHandler
        {
            public HttpLoggingHandler(HttpMessageHandler innerHandler)
                : base(innerHandler)
            {
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var property = (HttpTraceProperty)request.Properties[HttpTraceProperty.Key];
                var startTime = DateTime.UtcNow;
                Exception exception = null;
                HttpResponseMessage response = null;
                try
                {
                    request.Headers.Add("x-ms-request-id", property.ActivityId);
                    response = await base.SendAsync(request, cancellationToken);
                    return response;
                }
                catch (Exception ex)
                {
                    exception = ex;
                    throw;
                }
                finally
                {
                    var endTime = DateTime.UtcNow;
                    var startTimeValue = string.Format("{0:hh:mm.fff}", startTime);
                    var endTimeValue = string.Format("{0:hh:mm.fff}", endTime);
                    var latencyInMilliseconds = (int)(endTime - startTime).TotalMilliseconds;

                    var details = new StringBuilder();
                    details.AppendFormat("{0}", property.Details);
                    if (response != null)
                    {
                        details.AppendFormat(" ({0})", response.ReasonPhrase);
                    }
                    if (exception != null)
                    {
                        details.AppendFormat(" {0}", exception);
                    }

                    var tracer = (IScaleTracer)AppServiceEventSource.Instance;
                    tracer.TraceHttp(
                        property.ActivityId,
                        property.Worker,
                        request.Method.Method,
                        request.RequestUri.AbsolutePath,
                        response == null ? 0 : (int)response.StatusCode,
                        startTimeValue,
                        endTimeValue,
                        latencyInMilliseconds,
                        string.Empty,
                        details.ToString());
                }
            }
        }

        private class HttpTraceProperty
        {
            public const string Key = "HttpTraceProperty";

            public IWorkerInfo Worker { get; set; }

            public string ActivityId { get; set; }

            public string Details { get; set; }
        }
    }
}