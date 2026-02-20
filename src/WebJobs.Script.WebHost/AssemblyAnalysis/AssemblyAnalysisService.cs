// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.AssemblyAnalyzer
{
    internal class AssemblyAnalysisService : IHostedService, IDisposable
    {
        private readonly IEnvironment _environment;
        private readonly IOptionsMonitor<StandbyOptions> _standbyOptionsMonitor;
        private readonly WebJobsScriptHostService _scriptHost;
        private readonly string _scriptRootPath;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _analysisTask;
        private bool _disposed;
        private bool _analysisScheduled;
        private ILogger _logger;

        public AssemblyAnalysisService(IEnvironment environment, WebJobsScriptHostService scriptHost, ILoggerFactory loggerFactory, IOptionsMonitor<StandbyOptions> standbyOptionsMonitor, IOptions<ScriptApplicationHostOptions> applicationHostOptions)
        {
            _environment = environment;
            _scriptHost = scriptHost;
            _standbyOptionsMonitor = standbyOptionsMonitor;
            _scriptRootPath = applicationHostOptions?.Value?.ScriptPath;
            _logger = loggerFactory.CreateLogger<AssemblyAnalysisService>();
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

        protected virtual IScriptJobHost GetJobHost() => _scriptHost.GetService<IScriptJobHost>();

        private void ScheduleAssemblyAnalysis()
        {
            var jobHost = GetJobHost();

            if (jobHost == null
                || !jobHost.Functions.Any(f => f.Metadata.ScriptFile?.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) == true))
            {
                return;
            }

            _analysisScheduled = true;
            _cancellationTokenSource = new CancellationTokenSource();

            _analysisTask = Task.Delay(TimeSpan.FromMinutes(1), _cancellationTokenSource.Token)
               .ContinueWith(t => AnalyzeFunctionAssemblies());
        }

        internal void AnalyzeFunctionAssemblies()
        {
            try
            {
                var jobHost = GetJobHost();

                if (jobHost == null
                    || string.IsNullOrEmpty(_scriptRootPath)
                    || (_cancellationTokenSource?.IsCancellationRequested ?? false))
                {
                    return;
                }

                var hasUnoptimizedAssemblies = false;
                string normalizedRoot = Path.GetFullPath(_scriptRootPath);

                foreach (var item in jobHost.Functions)
                {
                    if (_cancellationTokenSource?.IsCancellationRequested ?? false)
                    {
                        return;
                    }

                    if (item.Metadata.ScriptFile?.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        // Isolated - check the function assembly on disk via ScriptFile path.
                        string scriptFilePath = Path.GetFullPath(Path.Combine(normalizedRoot, item.Metadata.ScriptFile));

                        if (!scriptFilePath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogWarning("Skipping assembly with path outside script root: {ScriptFile}", item.Metadata.ScriptFile);
                            continue;
                        }

                        if (!IsReadyToRunOptimized(scriptFilePath))
                        {
                            hasUnoptimizedAssemblies = true;
                            break;
                        }
                    }
                }

                if (hasUnoptimizedAssemblies)
                {
                    _logger.LogDiagnosticEventWarning(
                        DiagnosticEventConstants.FunctionAssemblyNotReadyToRunErrorCode,
                        "Function assemblies are not optimized with Ready-to-Run compilation, which may increase cold start times. Publish your application with PublishReadyToRun=true to improve performance.",
                        DiagnosticEventConstants.FunctionAssemblyNotReadyToRunHelpLink,
                        exception: null);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing function assemblies. Handling error and continuing.");
            }
        }

        private static bool IsReadyToRunOptimized(string assemblyPath)
        {
            try
            {
                using (var stream = File.OpenRead(assemblyPath))
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
