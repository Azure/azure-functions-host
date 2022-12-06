// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.AssemblyAnalyzer
{
    internal class AssemblyAnalysisService : IHostedService, IDisposable
    {
        private readonly IEnvironment _environment;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IOptionsMonitor<StandbyOptions> _standbyOptionsMonitor;
        private readonly WebJobsScriptHostService _scriptHost;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _analysisTask;
        private bool _disposed;
        private bool _analysisScheduled;
        private ILogger _logger;

        public AssemblyAnalysisService(IEnvironment environment, WebJobsScriptHostService scriptHost, ILoggerFactory loggerFactory, IOptionsMonitor<StandbyOptions> standbyOptionsMonitor)
        {
            _environment = environment;
            _scriptHost = scriptHost;
            _loggerFactory = loggerFactory;
            _standbyOptionsMonitor = standbyOptionsMonitor;
            _logger = _loggerFactory.CreateLogger<AssemblyAnalysisService>();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (!_environment.IsCoreTools())
                {
                    if (_standbyOptionsMonitor.CurrentValue.InStandbyMode)
                    {
                        _standbyOptionsMonitor.OnChange(standbyOptions =>
                        {
                            if (!standbyOptions.InStandbyMode && !_analysisScheduled)
                            {
                                ScheduleAssemblyAnalysis();
                            }
                        });
                    }
                    else
                    {
                        ScheduleAssemblyAnalysis();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting Assembly analysis service. Handling error and continuing.");
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _cancellationTokenSource?.Cancel();

                if (_analysisTask != null && !_analysisTask.IsCompleted)
                {
                    _logger.LogDebug("Assembly analysis service stopped before analysis completion. Waiting for cancellation.");

                    return _analysisTask;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping Assembly analysis service. Handling error and continuing.");
            }
            return Task.CompletedTask;
        }

        private void ScheduleAssemblyAnalysis()
        {
            var jobHost = _scriptHost.GetService<IScriptJobHost>();
            if (jobHost == null
                || !jobHost.Functions.Any(f => f.Metadata.IsDirect()))
            {
                return;
            }

            _analysisScheduled = true;
            _cancellationTokenSource = new CancellationTokenSource();

            _analysisTask = Task.Delay(TimeSpan.FromMinutes(1), _cancellationTokenSource.Token)
               .ContinueWith(t => AnalyzeFunctionAssemblies());
        }

        private void AnalyzeFunctionAssemblies()
        {
            var jobHost = _scriptHost.GetService<IScriptJobHost>();

            if (jobHost == null
                || _cancellationTokenSource.IsCancellationRequested)
            {
                return;
            }

            var logger = _loggerFactory.CreateLogger<AssemblyAnalysisService>();
            var assemblies = new HashSet<Assembly>();
            var hasUnoptimizedAssemblies = false;

            foreach (var item in jobHost.Functions)
            {
                if (_cancellationTokenSource.IsCancellationRequested)
                {
                    return;
                }

                if (item.Metadata.Properties.TryGetValue(ScriptConstants.FunctionMetadataDirectTypeKey, out Type type)
                    && !assemblies.Contains(type.Assembly))
                {
                    if (!IsReadyToRunOptimized(type.Assembly))
                    {
                        hasUnoptimizedAssemblies = true;
                        logger.Log(LogLevel.Debug, "Detected unoptimized function assemblies.");

                        break;
                    }

                    assemblies.Add(type.Assembly);
                }
            }

            if (!hasUnoptimizedAssemblies)
            {
                logger.Log(LogLevel.Debug, "All function assemblies optimized.");
            }
        }

        private static bool IsReadyToRunOptimized(Assembly assembly)
        {
            try
            {
                using (var stream = File.OpenRead(assembly.Location))
                using (var peFile = new PEReader(stream))
                {
                    return peFile.PEHeaders.CorHeader?.ManagedNativeHeaderDirectory.Size != 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _cancellationTokenSource?.Dispose();
                _disposed = true;
            }
        }
    }
}
