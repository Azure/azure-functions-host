// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Metrics
{
    public class LinuxContainerMetricsPublisher
    {
        public const string PublishMemoryActivityPath = "/memoryactivity";
        public const string PublishFunctionActivityPath = "/functionactivity";

        public const string ContainerNameHeader = "x-ms-functions-container-name";
        public const string HostNameHeader = "x-ms-functions-host-name";
        public const string SiteTokenHeader = "x-ms-site-restricted-token";
        public const string StampNameHeader = "x-ms-site-home-stamp";

        private const string _requestUriFormat = "https://{0}:31002/api/metrics";
        private const int _maxErrorLimit = 5;
        private const int _maxBufferSize = 50000;

        private readonly TimeSpan _memorySnapshotInterval = TimeSpan.FromMilliseconds(1000);
        private readonly TimeSpan _metricPublishInterval = TimeSpan.FromMilliseconds(30 * 1000);
        private readonly IOptionsMonitor<StandbyOptions> _standbyOptions;
        private readonly IDisposable _standbyOptionsOnChangeSubscription;
        private readonly string _requestUri;
        private readonly IEnvironment _environment;

        private string _containerName;
        private Timer _metricsPublisherTimer;
        private BlockingCollection<MemoryActivity> _memoryActivities;
        private BlockingCollection<FunctionActivity> _functionActivities;

        private ConcurrentQueue<MemoryActivity> _currentMemoryActivities;
        private ConcurrentQueue<FunctionActivity> _currentFunctionActivities;

        private HttpClient _httpClient;
        private Action<LogLevel, string> _logTraceEvent;
        private Process _process;
        private Timer _processMonitorTimer;
        private int _errorCount = 0;
        private string _hostName;
        private string _stampName;

        public LinuxContainerMetricsPublisher(IEnvironment environment, IOptionsMonitor<StandbyOptions> standbyOptions, Action<LogLevel, string> logMetricsPublishEvent, HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _standbyOptions = standbyOptions ?? throw new ArgumentNullException(nameof(standbyOptions));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _logTraceEvent = logMetricsPublishEvent ?? throw new ArgumentNullException(nameof(logMetricsPublishEvent));
            _memoryActivities = new BlockingCollection<MemoryActivity>(new ConcurrentQueue<MemoryActivity>(), boundedCapacity: _maxBufferSize);
            _functionActivities = new BlockingCollection<FunctionActivity>(new ConcurrentQueue<FunctionActivity>(), boundedCapacity: _maxBufferSize);

            _currentMemoryActivities = new ConcurrentQueue<MemoryActivity>();
            _currentFunctionActivities = new ConcurrentQueue<FunctionActivity>();

            var nodeIpAddress = _environment.GetEnvironmentVariable(EnvironmentSettingNames.NodeIpAddress);
            _requestUri = string.Format(_requestUriFormat, nodeIpAddress);

            _containerName = _environment.GetEnvironmentVariable(EnvironmentSettingNames.ContainerName);

            if (_standbyOptions.CurrentValue.InStandbyMode)
            {
                _standbyOptionsOnChangeSubscription = _standbyOptions.OnChange(o => OnStandbyOptionsChange());
            }
            else
            {
                StartMetricsPublisher();
            }
        }

        private void OnStandbyOptionsChange()
        {
            if (!_standbyOptions.CurrentValue.InStandbyMode)
            {
                StartMetricsPublisher();
            }
        }

        public void AddFunctionExecutionActivity(string functionName, string invocationId, int concurrency, string executionStage, bool success, long executionTimeSpan, DateTime eventTimeStamp)
        {
            FunctionActivity activity = new FunctionActivity
            {
                FunctionName = functionName,
                InvocationId = invocationId,
                Concurrency = concurrency,
                ExecutionStage = executionStage,
                IsSucceeded = success,
                ExecutionTimeSpanInMs = executionTimeSpan,
                EventTimeStamp = eventTimeStamp,
                Tenant = _environment.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteStampDeploymentId)?.ToLowerInvariant()
            };

            if (!_functionActivities.TryAdd(activity))
            {
                _logTraceEvent(LogLevel.Warning, string.Format("LinuxContainerMetricsPublisher: Buffer full. Dropping current batch of function activities"));
                DrainActivities(_currentFunctionActivities, _functionActivities);
            }

            _logTraceEvent(LogLevel.Information, string.Format("LinuxContainerMetricsPublisher: Added function activity : {0} {1} {2} {3} {4} {5}", functionName, invocationId, concurrency, executionStage, success, executionTimeSpan));
        }

        public void AddMemoryActivity(DateTime timeStampUtc, long data)
        {
            var memoryActivity = new MemoryActivity
            {
                CommitSizeInBytes = data,
                EventTimeStamp = timeStampUtc,
                Tenant = _environment.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteStampDeploymentId)?.ToLowerInvariant()
            };

            if (!_memoryActivities.TryAdd(memoryActivity))
            {
                _logTraceEvent(LogLevel.Warning, string.Format("LinuxContainerMetricsPublisher: Buffer full. Dropping current batch of memory activities"));
                DrainActivities(_currentMemoryActivities, _memoryActivities);
            }
        }

        private void DrainActivities<T>(ConcurrentQueue<T> currentActivites, BlockingCollection<T> activitiesToProcess)
        {
            currentActivites.Clear();

            if (activitiesToProcess.Any())
            {
                while (activitiesToProcess.TryTake(out T item))
                {
                    currentActivites.Enqueue(item);
                }
            }
        }

        internal void OnFunctionMetricsPublishTimer(object state)
        {
            PublishActivity(_currentMemoryActivities, _memoryActivities, PublishMemoryActivityPath);
            PublishActivity(_currentFunctionActivities, _functionActivities, PublishFunctionActivityPath);
        }

        private void PublishActivity<T>(ConcurrentQueue<T> currentActivities, BlockingCollection<T> activitiesToProcess, string publishPath)
        {
            try
            {
                if (currentActivities.Any())
                {
                    SendRequest(currentActivities, publishPath);
                }
                DrainActivities(currentActivities, activitiesToProcess);
            }
            catch (Exception ex)
            {
                _errorCount++;
                _logTraceEvent(LogLevel.Warning, string.Format("LinuxContainerMetricsPublisher : Error publishing activites. Exception : {0}", ex.ToString()));
                if (_errorCount < _maxErrorLimit)
                {
                    _logTraceEvent(LogLevel.Warning, string.Format("LinuxContainerMetricsPublisher : Error count {0} within threshold. will retry later.", _errorCount));
                }
                else
                {
                    _logTraceEvent(LogLevel.Error, string.Format("LinuxContainerMetricsPublisher : Max error limit reached. Draining activities for {0}", _containerName));
                    DrainActivities(currentActivities, activitiesToProcess);
                    _errorCount = 0;
                }
            }
        }

        private void SendRequest<T>(ConcurrentQueue<T> activitiesToPublish, string publishPath)
        {
            var request = BuildRequest(HttpMethod.Post, publishPath, activitiesToPublish.ToArray());
            HttpResponseMessage response = _httpClient.SendAsync(request).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
        }

        public void InitializeRequestHeaders()
        {
            _hostName = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName);
            _stampName = _environment.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteHomeStampName);
            _process = Process.GetCurrentProcess();
        }

        public void StartMetricsPublisher()
        {
            InitializeRequestHeaders();
            _processMonitorTimer = new Timer(OnProcessMonitorTimer, null, TimeSpan.Zero, _memorySnapshotInterval);
            _metricsPublisherTimer = new Timer(OnFunctionMetricsPublishTimer, null, TimeSpan.Zero, _metricPublishInterval);

            _logTraceEvent(LogLevel.Information, string.Format("LinuxContainerMetricsPublisher: Starting metrics publisher for container : {0}", _containerName));
        }

        private void OnProcessMonitorTimer(object state)
        {
            _process.Refresh();
            var commitSizeBytes = _process.WorkingSet64;
            if (commitSizeBytes != 0)
            {
                AddMemoryActivity(DateTime.UtcNow, commitSizeBytes);
            }
        }

        private HttpRequestMessage BuildRequest<TContent>(HttpMethod method, string path, TContent content)
        {
            var request = new HttpRequestMessage(method, _requestUri + path)
            {
                Content = new ObjectContent<TContent>(content, new JsonMediaTypeFormatter())
            };

            var token = SimpleWebTokenHelper.CreateToken(DateTime.UtcNow.AddMinutes(5));

            // add the required authentication headers
            request.Headers.Add(ContainerNameHeader, _containerName);
            request.Headers.Add(HostNameHeader, _hostName);
            request.Headers.Add(SiteTokenHeader, token);
            request.Headers.Add(StampNameHeader, _stampName);

            return request;
        }
    }
}