// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IApplicationLifetime = Microsoft.AspNetCore.Hosting.IApplicationLifetime;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    internal class WorkerConcurrencyManager : IHostedService, IDisposable
    {
        private readonly TimeSpan _logStateInterval = TimeSpan.FromSeconds(60);
        private readonly ILogger _logger;
        private readonly IFunctionInvocationDispatcherFactory _functionInvocationDispatcherFactory;
        private readonly IEnvironment _environment;
        private readonly IApplicationLifetime _applicationLifetime;
        private readonly long _memoryLimit = AppServicesHostingUtility.GetMemoryLimitBytes();
        private readonly IOptionsMonitor<FunctionsHostingConfigOptions> _functionsHostingConfigOptionsMonitor;
        private readonly Func<Task> _stopApplication;

        private IOptions<WorkerConcurrencyOptions> _workerConcurrencyOptions;
        private IFunctionInvocationDispatcher _functionInvocationDispatcher;
        private System.Timers.Timer _timer;
        private ValueStopwatch _addWorkerStopwatch = ValueStopwatch.StartNew();
        private ValueStopwatch _logStateStopWatch = ValueStopwatch.StartNew();
        private bool _disposed = false;
        private IDisposable _hostingConfigOnChange;

        public WorkerConcurrencyManager(
            IFunctionInvocationDispatcherFactory functionInvocationDispatcherFactory,
            IEnvironment environment,
            IOptions<WorkerConcurrencyOptions> workerConcurrencyOptions,
            IOptionsMonitor<FunctionsHostingConfigOptions> functionsHostingConfigOptionsMonitor,
            IApplicationLifetime applicationLifetime,
            ILoggerFactory loggerFactory)
        {
            _functionInvocationDispatcherFactory = functionInvocationDispatcherFactory ?? throw new ArgumentNullException(nameof(functionInvocationDispatcherFactory));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _workerConcurrencyOptions = workerConcurrencyOptions;
            _functionsHostingConfigOptionsMonitor = functionsHostingConfigOptionsMonitor;
            _applicationLifetime = applicationLifetime;
            _logger = loggerFactory?.CreateLogger(LogCategories.Concurrency);
            _stopApplication = StopApplication;
            _stopApplication = _stopApplication.Debounce(1000);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            string workerRuntime = _environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime);
            if (!string.IsNullOrEmpty(workerRuntime))
            {
                // the feature applies only to "node","powershell","python"
                workerRuntime = workerRuntime.ToLower();
                if (workerRuntime == RpcWorkerConstants.NodeLanguageWorkerName
                || workerRuntime == RpcWorkerConstants.PowerShellLanguageWorkerName
                || workerRuntime == RpcWorkerConstants.PythonLanguageWorkerName)
                {
                    _functionInvocationDispatcher = _functionInvocationDispatcherFactory.GetFunctionDispatcher();

                    if (_functionInvocationDispatcher is HttpFunctionInvocationDispatcher)
                    {
                        _logger.LogDebug($"Http dynamic worker concurrency is not supported.");
                        return Task.CompletedTask;
                    }
                    if (!string.IsNullOrEmpty(_environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionsWorkerProcessCountSettingName)))
                    {
                        return Task.CompletedTask;
                    }
                    if (_environment.IsWorkerDynamicConcurrencyEnabled())
                    {
                        _logger.LogDebug($"Dynamic worker concurrency monitoring is starting by app setting.");
                        Activate();
                    }
                    else if (_functionsHostingConfigOptionsMonitor.CurrentValue != null && _functionsHostingConfigOptionsMonitor.CurrentValue.FunctionsWorkerDynamicConcurrencyEnabled)
                    {
                        _logger.LogDebug($"Dynamic worker concurrency monitoring is starting by hosting config.");
                        Activate();

                        _hostingConfigOnChange = _functionsHostingConfigOptionsMonitor.OnChange(async (newOptions) =>
                        {
                            if (!newOptions.FunctionsWorkerDynamicConcurrencyEnabled)
                            {
                                // There is a known issue when OnChange fires twice: https://github.com/dotnet/aspnetcore/issues/2542
                                // Lets make sure we stopped the app once
                                await _stopApplication();
                            }
                        });
                    }
                }
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            if (_timer != null)
            {
                _logger.LogDebug("Stopping dynamic worker concurrency monitoring.");
                _timer.Stop();
            }
            return Task.CompletedTask;
        }

        internal async void OnTimer(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (_environment.IsPlaceholderModeEnabled() || _disposed)
            {
                return;
            }

            try
            {
                var workerStatuses = await _functionInvocationDispatcher.GetWorkerStatusesAsync();

                if (NewWorkerIsRequired(workerStatuses, _addWorkerStopwatch.GetElapsedTime()))
                {
                    if (_functionInvocationDispatcher is RpcFunctionInvocationDispatcher rpcDispatcher)
                    {
                        var allWorkerChannels = await rpcDispatcher.GetAllWorkerChannelsAsync();
                        if (CanScale(allWorkerChannels) && IsEnoughMemoryToScale(Process.GetCurrentProcess().PrivateMemorySize64,
                            allWorkerChannels.Select(x => x.WorkerProcess.Process.PrivateMemorySize64),
                            _memoryLimit))
                        {
                            await _functionInvocationDispatcher.StartWorkerChannel();
                            _addWorkerStopwatch = ValueStopwatch.StartNew();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // don't allow background exceptions to escape
                _logger.LogError(ex, "Error monitoring worker concurrency");
            }
            _timer.Start();
        }

        private void Activate()
        {
            _logger.LogDebug("Starting dynamic worker concurrency monitoring.");
            _timer = new System.Timers.Timer()
            {
                AutoReset = false,
                Interval = _workerConcurrencyOptions.Value.CheckInterval.TotalMilliseconds,
            };

            _timer.Elapsed += OnTimer;
            _timer.Start();
        }

        internal bool NewWorkerIsRequired(IDictionary<string, WorkerStatus> workerStatuses, TimeSpan timeSinceLastNewWorker)
        {
            if (timeSinceLastNewWorker < _workerConcurrencyOptions.Value.AdjustmentPeriod)
            {
                return false;
            }

            bool result = false;
            if (workerStatuses.All(x => x.Value.IsReady))
            {
                // Check how many channels are overloaded
                List<WorkerStatusDetails> descriptions = new List<WorkerStatusDetails>();
                foreach (string key in workerStatuses.Keys)
                {
                    WorkerStatus workerStatus = workerStatuses[key];
                    bool overloaded = IsOverloaded(workerStatus);
                    descriptions.Add(new WorkerStatusDetails()
                    {
                        WorkerId = key,
                        WorkerStatus = workerStatus,
                        IsOverloaded = overloaded
                    });
                }

                int overloadedCount = descriptions.Where(x => x.IsOverloaded == true).Count();
                if (overloadedCount > 0)
                {
                    if (workerStatuses.Count() < _workerConcurrencyOptions.Value.MaxWorkerCount)
                    {
                        _logger.LogInformation($"A new worker will be added, overloaded workers = {overloadedCount}, initialized workers = {workerStatuses.Count()} ");
                        result = true;
                    }
                }

                if (result == true || _logStateStopWatch.GetElapsedTime() > _logStateInterval)
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (WorkerStatusDetails description in descriptions)
                    {
                        sb.Append(FormatWorkerDescription(description));
                        sb.Append(Environment.NewLine);
                    }
                    _logStateStopWatch = ValueStopwatch.StartNew();
                    _logger.LogDebug(sb.ToString());
                }
            }

            return result;
        }

        internal bool IsOverloaded(WorkerStatus status)
        {
            if (status.LatencyHistory != null && (status.LatencyHistory.Count() >= _workerConcurrencyOptions.Value.HistorySize))
            {
                int overloadedCount = status.LatencyHistory.Where(x => x.TotalMilliseconds >= _workerConcurrencyOptions.Value.LatencyThreshold.TotalMilliseconds).Count();
                double proportion = (double)overloadedCount / _workerConcurrencyOptions.Value.HistorySize;

                return proportion >= _workerConcurrencyOptions.Value.NewWorkerThreshold;
            }
            return false;
        }

        internal string FormatWorkerDescription(WorkerStatusDetails desc)
        {
            string formattedLoadHistory = string.Empty, formattedLatencyHistory = string.Empty;
            double latencyAvg = 0, latencyMax = 0;
            if (desc.WorkerStatus != null && desc.WorkerStatus.LatencyHistory != null)
            {
                formattedLatencyHistory = string.Join(",", desc.WorkerStatus.LatencyHistory.Select(x => Math.Round(x.TotalMilliseconds, 0)));
                latencyMax = desc.WorkerStatus.LatencyHistory.Count() > 0 ? Math.Round(desc.WorkerStatus.LatencyHistory.Select(x => x.TotalMilliseconds).Max(), 0) : -1;
                if (desc.WorkerStatus.LatencyHistory.Count() > 1)
                {
                    latencyAvg = Math.Round(desc.WorkerStatus.LatencyHistory.Select(x => x.TotalMilliseconds).Average(), 0);
                }
            }

            return $@"Worker process stats: ProcessId={desc.WorkerId}, Overloaded={desc.IsOverloaded} 
LatencyHistory=({formattedLatencyHistory}), AvgLatency={latencyAvg}, MaxLatency={latencyMax}";
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _timer?.Dispose();
                    _hostingConfigOnChange?.Dispose();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        internal bool IsEnoughMemoryToScale(long hostProcessSize, IEnumerable<long> workerChannelSizes, long memoryLimit)
        {
            if (memoryLimit <= 0)
            {
                return true;
            }

            // Checking memory before adding a new worker
            // By adding `maxWorkerSize` to current memeory consumption we are predicting what will be overall memory consumption after adding a new worker.
            // We do not want this value to be more then 80%.
            long maxWorkerSize = workerChannelSizes.Max();
            long currentMemoryConsumption = workerChannelSizes.Sum() + hostProcessSize;
            if (currentMemoryConsumption + maxWorkerSize > memoryLimit * 0.8)
            {
                _logger.LogDebug($"Starting new language worker canceled: TotalMemory={memoryLimit}, MaxWorkerSize={maxWorkerSize}, CurrentMemoryConsumption={currentMemoryConsumption}");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Checks if a new worker can be added.
        /// </summary>
        /// <param name="workerChannels">All current worker channels.</param>
        /// <returns>True if a new worker can be started.</returns>
        internal bool CanScale(IEnumerable<IRpcWorkerChannel> workerChannels)
        {
            // Cancel if there is any "non-ready" channel.
            // A "ready" channel means it's ready for invocations.
            var nonReadyWorkerChannels = workerChannels.Where(x => x.IsChannelReadyForInvocations() == false);
            if (nonReadyWorkerChannels.Any())
            {
                _logger.LogDebug($"Starting new language worker canceled as there is atleast one non ready channel: TotalChannels={workerChannels.Count()}, NonReadyChannels={nonReadyWorkerChannels.Count()}");
                return false;
            }

            // Cancel if MaxWorkerCount is reached.
            int workersCount = workerChannels.Count();
            if (workersCount >= _workerConcurrencyOptions.Value.MaxWorkerCount)
            {
                _logger.LogDebug($"Starting new language worker canceled as the count of total channels reaches the maximum limit: TotalChannels={workersCount}, MaxWorkerCount={_workerConcurrencyOptions.Value.MaxWorkerCount}");
                return false;
            }

            return true;
        }

        private async Task StopApplication()
        {
            _logger.LogDebug($"Dynamic worker concurrency monitoring is stopping on hosting config update. Shutting down Functions Host.");
            await _functionInvocationDispatcher.ShutdownAsync();
            _applicationLifetime.StopApplication();
        }

        internal class WorkerStatusDetails
        {
            public string WorkerId { get; set; }

            public WorkerStatus WorkerStatus { get; set; }

            public bool IsOverloaded { get; set; }
        }
    }
}
