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
        private readonly IComposingService _composingService;

        public WorkerHarnessExecutor(IOptions<WorkerDescription> workerDescription,
            IWorkerProcessBuilder workerProcessBuilder,
            IScenarioParser scenarioParser,
            IComposingService composingService)
        {
            _workerDescription = workerDescription.Value;
            _workerProcessBuilder = workerProcessBuilder;
            _scenarioParser = scenarioParser;
            _composingService = composingService;
        }

        public async Task<bool> StartScenario(string scenarioFile)
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
                Console.WriteLine($"\n\n{ex}");

                return false;
            }
        }

        public async Task<bool> Start(string composeFile)
        {
            Process myProcess = _workerProcessBuilder.Build(_workerDescription);

            Compose executionContext = _composingService.Compose(composeFile);

            try
            {
                myProcess.Start();

                foreach (Instruction scenarioContext in executionContext.Instructions)
                {
                    Scenario scenario = _scenarioParser.Parse(scenarioContext);

                    foreach (IAction action in scenario.Actions)
                    {
                        await action.ExecuteAsync();
                    }
                }
               
                myProcess.Kill();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n\n{ex}");

                return false;
            }
        }
    }
}
