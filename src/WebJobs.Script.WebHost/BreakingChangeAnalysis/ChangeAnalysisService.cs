// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using DotNetTI.BreakingChangeAnalysis;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.ChangeAnalysis
{
    public sealed class ChangeAnalysisService : IHostedService, IBreakingChangeAnalysisService, IDisposable
    {
        private readonly IEnvironment _environment;
        private readonly IPrimaryHostStateProvider _hostStateProvider;
        private readonly IChangeAnalysisStateProvider _changeAnalysisStateProvider;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _analysisSemaphore;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private Task _analysisTask;
        private bool _disposed = false;

        public ChangeAnalysisService(ILogger<ChangeAnalysisService> logger,
                                     IEnvironment environment,
                                     IChangeAnalysisStateProvider changeAnalysisStateProvider,
                                     IPrimaryHostStateProvider hostStateProvider)
        {
            _environment = environment;
            _hostStateProvider = hostStateProvider;
            _changeAnalysisStateProvider = changeAnalysisStateProvider;
            _logger = logger;
            _analysisSemaphore = new SemaphoreSlim(1, 1);
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (!_environment.IsPlaceholderModeEnabled() && !_environment.IsCoreTools())
            {
                ScheduleBreakChangeAnalysis();
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _cancellationTokenSource.Cancel();

            if (_analysisTask != null && !_analysisTask.IsCompleted)
            {
                _logger.LogInformation("Change analysis service stopped before analysis completion. Waiting for cancellation");
                return _analysisTask;
            }

            return Task.CompletedTask;
        }

        private void ScheduleBreakChangeAnalysis()
        {
            _analysisTask = Task.Delay(TimeSpan.FromMinutes(1), _cancellationTokenSource.Token)
                .ContinueWith(async t =>
                {
                    if (!_cancellationTokenSource.IsCancellationRequested && _hostStateProvider.IsPrimary)
                    {
                        await TryLogBreakingChangeReportAsync(_cancellationTokenSource.Token);
                    }
                })
                .Unwrap();
        }

        internal async Task TryLogBreakingChangeReportAsync(CancellationToken cancellationToken)
        {
            bool lockAcquired = false;
            try
            {
                lockAcquired = await _analysisSemaphore.WaitAsync(30000, cancellationToken);

                if (!lockAcquired)
                {
                    _logger.LogWarning("Unable to acquire change analysis process lock. Skipping analysis.");
                    return;
                }

                ChangeAnalysisState analysisState = await _changeAnalysisStateProvider.GetCurrentAsync(cancellationToken);

                // Currently, only performing analysis if we haven't done so within the previous 7 days
                if (analysisState.LastAnalysisTime > DateTimeOffset.UtcNow.AddDays(-7))
                {
                    _logger.LogInformation("Skipping breaking change analysis.");
                    return;
                }

                _logger.LogInformation("Initiating breaking change analysis...");

                LogBreakingChangeReport(cancellationToken);

                await _changeAnalysisStateProvider.SetTimestampAsync(DateTimeOffset.UtcNow, analysisState.Handle, cancellationToken);

                _logger.LogInformation("Breaking change analysis operation completed.");
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Breaking change analysis operation cancelled.");
            }
            catch (Exception exc)
            {
                _logger.LogWarning(exc, "Breaking change analysis operation failed");
            }
            finally
            {
                if (lockAcquired)
                {
                    _analysisSemaphore.Release();
                }
            }
        }

        public IEnumerable<AssemblyReport> LogBreakingChangeReport(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var generator = new BreakingChangeReportGenerator();

            IEnumerable<Assembly> functionContextAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => AssemblyLoadContext.GetLoadContext(a) == FunctionAssemblyLoadContext.Shared);

            var reports = new List<AssemblyReport>();
            foreach (var assembly in functionContextAssemblies)
            {
                cancellationToken.ThrowIfCancellationRequested();

                AssemblyReport report = generator.ProduceReport(new Uri(assembly.Location).LocalPath);
                _logger.LogDebug(JsonConvert.SerializeObject(report));
                reports.Add(report);
            }

            return reports;
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _analysisSemaphore.Dispose();
                    _cancellationTokenSource.Dispose();
                }

                _disposed = true;
            }
        }

        public void Dispose() => Dispose(true);
    }
}
