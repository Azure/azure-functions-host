// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace WorkerHarness.Core
{
    public class WorkerHarnessExecutor
    {
        private readonly WorkerDescription _workerDescription;
        private readonly IWorkerProcessBuilder _workerProcessBuilder;
        private readonly IScenarioParser _scenarioParser;

        public WorkerHarnessExecutor(IOptions<WorkerDescription> workerDescription,
            IWorkerProcessBuilder workerProcessBuilder,
            IScenarioParser scenarioParser)
        {
            _workerDescription = workerDescription.Value;
            _workerProcessBuilder = workerProcessBuilder;
            _scenarioParser = scenarioParser;
        }

        public async Task<bool> Start(string scenarioFile)
        {

            Process myProcess = _workerProcessBuilder.Build(_workerDescription);

            Scenario scenario = _scenarioParser.Parse(scenarioFile);

            try
            {
                myProcess.Start();

                foreach (IAction action in scenario.Actions)
                {
                    await action.ExecuteAsync();
                }

                myProcess.Kill();
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
