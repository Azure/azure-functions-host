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
    public class LinuxContainerMetricsPublisher : IMetricsPublisher
    {
        public const string PublishMemoryActivityPath = "/memoryactivity";
        public const string PublishFunctionActivityPath = "/functionactivity";

        public const string ContainerNameHeader = "x-ms-functions-container-name";
        public const string HostNameHeader = "x-ms-functions-host-name";
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

        // Buffer for all memory activities for this container.
        private BlockingCollection<MemoryActivity> _memoryActivities;

        // Buffer for all function activities for this container.
        private BlockingCollection<FunctionActivity> _functionActivities;

        // The current batch of memory activities we are trying to publish.
        private ConcurrentQueue<MemoryActivity> _currentMemoryActivities;

        // The current batch of function activities we are trying to publish.
        private ConcurrentQueue<FunctionActivity> _currentFunctionActivities;

        private HttpClient _httpClient;
        private ILogger _logger;
        private HostNameProvider _hostNameProvider;
        private string _tenant;
        private Process _process;
        private Timer _processMonitorTimer;
        private string _containerName;
        private Timer _metricsPublisherTimer;
        private int _errorCount = 0;
        private string _stampName;

        public LinuxContainerMetricsPublisher(IEnvironment environment, IOptionsMonitor<StandbyOptions> standbyOptions, ILogger<LinuxContainerMetricsPublisher> logger, HttpClient httpClient, HostNameProvider hostNameProvider)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _standbyOptions = standbyOptions ?? throw new ArgumentNullException(nameof(standbyOptions));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _hostNameProvider = hostNameProvider ?? throw new ArgumentNullException(nameof(hostNameProvider));
            _memoryActivities = new BlockingCollection<MemoryActivity>(new ConcurrentQueue<MemoryActivity>(), boundedCapacity: _maxBufferSize);
            _functionActivities = new BlockingCollection<FunctionActivity>(new ConcurrentQueue<FunctionActivity>(), boundedCapacity: _maxBufferSize);

            _currentMemoryActivities = new ConcurrentQueue<MemoryActivity>();
            _currentFunctionActivities = new ConcurrentQueue<FunctionActivity>();

            var nodeIpAddress = _environment.GetEnvironmentVariable(EnvironmentSettingNames.LinuxNodeIpAddress);
            _requestUri = string.Format(_requestUriFormat, nodeIpAddress);

            _containerName = _environment.GetEnvironmentVariable(EnvironmentSettingNames.ContainerName);

            if (_standbyOptions.CurrentValue.InStandbyMode)
            {
                _standbyOptionsOnChangeSubscription = _standbyOptions.OnChange(o => OnStandbyOptionsChange());
            }
            else
            {
                Start();
            }
        }

        private void OnStandbyOptionsChange()
        {
            if (!_standbyOptions.CurrentValue.InStandbyMode)
            {
                Start();
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
                Tenant = _tenant
            };

            if (!_functionActivities.TryAdd(activity))
            {
                _logger.LogWarning($"Buffer for function activities is full with {_functionActivities.Count} elements. Dropping current batch of function activities");
                DrainActivities(_currentFunctionActivities, _functionActivities);
            }

            _logger.LogDebug($"Added function activity : {functionName} {invocationId} {concurrency} {executionStage} {success} {executionTimeSpan}");
        }

        public void AddMemoryActivity(DateTime timeStampUtc, long data)
        {
            var memoryActivity = new MemoryActivity
            {
                CommitSizeInBytes = data,
                EventTimeStamp = timeStampUtc,
                Tenant = _tenant
            };

            if (!_memoryActivities.TryAdd(memoryActivity))
            {
                _logger.LogWarning($"Buffer for holding memory activities is full with {_memoryActivities.Count} elements.Dropping current batch of memory activities");
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

        internal async void OnFunctionMetricsPublishTimer(object state)
        {
            await PublishActivity(_currentMemoryActivities, _memoryActivities, PublishMemoryActivityPath);
            await PublishActivity(_currentFunctionActivities, _functionActivities, PublishFunctionActivityPath);
        }

        private async Task PublishActivity<T>(ConcurrentQueue<T> currentActivities, BlockingCollection<T> activitiesToProcess, string publishPath)
        {
            try
            {
                if (currentActivities.Any())
                {
                    await SendRequest(currentActivities, publishPath);
                }
                DrainActivities(currentActivities, activitiesToProcess);
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                _errorCount++;
                _logger.LogError(ex, "Error publishing activites");

                if (_errorCount < _maxErrorLimit)
                {
                    _logger.LogWarning(ex, string.Format("Error count {0} within threshold. Will retry this batch later.", _errorCount));
                }
                else
                {
                    _logger.LogError(string.Format("LinuxContainerMetricsPublisher : Max error limit reached. Draining current activities for {0}", _containerName));
                    DrainActivities(currentActivities, activitiesToProcess);
                    _errorCount = 0;
                }
            }
        }

        internal async Task SendRequest<T>(ConcurrentQueue<T> activitiesToPublish, string publishPath)
        {
            var request = BuildRequest(HttpMethod.Post, publishPath, activitiesToPublish.ToArray());
            HttpResponseMessage response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        public void Initialize()
        {
            _stampName = _environment.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteHomeStampName);
            _tenant = _environment.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteStampDeploymentId)?.ToLowerInvariant();
            _process = Process.GetCurrentProcess();
        }

        public void Start()
        {
            Initialize();
            _processMonitorTimer = new Timer(OnProcessMonitorTimer, null, TimeSpan.Zero, _memorySnapshotInterval);
            _metricsPublisherTimer = new Timer(OnFunctionMetricsPublishTimer, null, TimeSpan.Zero, _metricPublishInterval);

            _logger.LogInformation(string.Format("Starting metrics publisher for container : {0}", _containerName));
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
            request.Headers.Add(HostNameHeader, _hostNameProvider.Value);
            request.Headers.Add(ScriptConstants.SiteTokenHeaderName, token);
            request.Headers.Add(StampNameHeader, _stampName);

            return request;
        }
    }
}