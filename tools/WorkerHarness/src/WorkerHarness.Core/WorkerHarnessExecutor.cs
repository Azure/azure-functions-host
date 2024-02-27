// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerHarness.Core.Actions;
using WorkerHarness.Core.Diagnostics;
using WorkerHarness.Core.GrpcService;
using WorkerHarness.Core.Options;
using WorkerHarness.Core.Parsing;
using WorkerHarness.Core.Profiling;
using WorkerHarness.Core.Variables;
using WorkerHarness.Core.WorkerProcess;

namespace WorkerHarness.Core
{
    public sealed class WorkerHarnessExecutor : IWorkerHarnessExecutor
    {
        private readonly IWorkerProcessBuilder _workerProcessBuilder;
        private readonly IScenarioParser _scenarioParser;
        private readonly ILogger<WorkerHarnessExecutor> _logger;
        private readonly HarnessOptions _harnessOptions;
        private readonly IVariableObservable _globalVariables;
        private readonly Uri _serverUri;
        private readonly IProfilerFactory _profilerFactory;

        public WorkerHarnessExecutor(
            IWorkerProcessBuilder workerProcessBuilder,
            IScenarioParser scenarioParser,
            ILogger<WorkerHarnessExecutor> logger,
            IOptions<HarnessOptions> harnessOptions,
            IVariableObservable variableObservable,
            IGrpcServer grpcServer,
            IProfilerFactory profilerFactory)
        {
            _workerProcessBuilder = workerProcessBuilder;
            _scenarioParser = scenarioParser;
            _logger = logger;
            _harnessOptions = harnessOptions.Value;
            _globalVariables = variableObservable;
            _serverUri = grpcServer.Uri;
            _profilerFactory = profilerFactory;
        }
        public async Task<bool> StartAsync()
        {
            var lanugageExecutableDirectory = Path.GetDirectoryName(_harnessOptions.LanguageExecutable);

            WorkerContext context = new(_harnessOptions.LanguageExecutable!,
                _harnessOptions.LanguageExecutableArguments,
                _harnessOptions.WorkerPath!,
                _harnessOptions.WorkerArguments,
                lanugageExecutableDirectory,
                _serverUri);

            IWorkerProcess myProcess = _workerProcessBuilder.Build(context);

            ExecutionContext executionContext = new(_globalVariables, _scenarioParser, myProcess)
            {
                DisplayVerboseError = _harnessOptions.DisplayVerboseError,
                Profiler = _profilerFactory.CreateProfiler()
            };

            try
            {
                Scenario scenario = _scenarioParser.Parse(_harnessOptions.ScenarioFile!);

                myProcess.Start();

                _logger.LogDebug("Worker process started with process id: {0}", myProcess.Id);
                _logger.LogInformation("Executing scenario: {0}", scenario.ScenarioName);

                foreach (IAction action in scenario.Actions)
                {
                    var actionResult = await action.ExecuteAsync(executionContext!);

                    if (!_harnessOptions.ContinueUponFailure && actionResult.Status == StatusCode.Failure)
                    {
                        break;
                    }
                }

                HarnessEventSource.Log.ScenarioExecutionFinished("foo");
                _logger.LogInformation("Executing scenario: {0} finished.", scenario.ScenarioName);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogCritical("An exception occurs: \n{0}\n{1}", ex.Message, ex.StackTrace);

                return false;
            }
            finally
            {
                await CleanupProfilingIfNeeded(executionContext);

                myProcess.Dispose();
            }
        }

        private static async Task CleanupProfilingIfNeeded(ExecutionContext executionContext)
        {
            if (executionContext.Profiler == null)
            {
                return;
            }

            // Safe to call StopProfiling to ensure we are cleaning up in case the stop profiling prop was not set in any of the actions.
            if (executionContext.Profiler.Status != ProfilingStatus.Stopped)
            {
                await executionContext.Profiler.StopProfilingAsync();
            }

            if (executionContext.Profiler is IDisposable profiler)
            {
                profiler.Dispose();
            }
        }
    }
}
