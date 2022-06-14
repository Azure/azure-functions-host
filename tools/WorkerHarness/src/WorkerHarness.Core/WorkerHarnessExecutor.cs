// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace WorkerHarness.Core
{
    public class WorkerHarnessExecutor
    {
        private WorkerDescription _workerDescription;
        private IWorkerProcessBuilder _workerProcessBuilder;
        private IScenarioParser _scenarioParser;

        public WorkerHarnessExecutor(IOptions<WorkerDescription> workerDescription,
            IWorkerProcessBuilder workerProcessBuilder,
            IScenarioParser scenarioParser)
        {
            _workerDescription = workerDescription.Value;
            _workerProcessBuilder = workerProcessBuilder;
            _scenarioParser = scenarioParser;
        }

        public bool Start(string scenarioFile)
        {

            Process myProcess = _workerProcessBuilder.Build(_workerDescription);

            Scenario scenario = _scenarioParser.Parse(scenarioFile);

            try
            {
                myProcess.Start();
                Console.WriteLine($"A {_workerDescription.Language} process is starting...");

                foreach (IAction action in scenario.Actions)
                {
                    action.Execute();
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);

                return false;
            }
        }
    }
}
