// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WorkerHarness.Core.Parsing;
using WorkerHarness.Core.Profiling;
using WorkerHarness.Core.Variables;
using WorkerHarness.Core.WorkerProcess;

namespace WorkerHarness.Core
{
    public sealed class ExecutionContext
    {
        public ExecutionContext(IVariableObservable variableManager, IScenarioParser scenarioParser,
            IWorkerProcess workerProcess)
        {
            GlobalVariables = variableManager;
            ScenarioParser = scenarioParser;
            WorkerProcess = workerProcess;
        }

        public IVariableObservable GlobalVariables { get; private set; }
        public IScenarioParser ScenarioParser { get; private set; }
        public IWorkerProcess WorkerProcess { get; private set; }

        /// <summary>
        /// The <see cref="IProfiler"/> instance associated with the current execution.
        /// </summary>
        public IProfiler? Profiler { get; set; }
        
        public bool DisplayVerboseError { get; set; } = false;

        public bool ContinueUponFailure { get; set; } = false;
    }
}