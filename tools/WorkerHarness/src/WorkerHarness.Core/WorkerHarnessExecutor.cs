// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace WorkerHarness.Core
{
    public class DefaultWorkerHarnessExecutor : IWorkerHarnessExecutor
    {
        private readonly WorkerDescription _workerDescription;
        private readonly IWorkerProcessBuilder _workerProcessBuilder;
        private readonly IScenarioParser _scenarioParser;
        private readonly IActionWriter _actionWriter;

        public DefaultWorkerHarnessExecutor(IOptions<WorkerDescription> workerDescription,
            IWorkerProcessBuilder workerProcessBuilder,
            IScenarioParser scenarioParser,
            IActionWriter actionWriter)
        {
            _workerDescription = workerDescription.Value;
            _workerProcessBuilder = workerProcessBuilder;
            _scenarioParser = scenarioParser;
            _actionWriter = actionWriter;
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
                    ActionResult result = await action.ExecuteAsync();

                    ShowActionResult(result);
                    
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

        private void ShowActionResult(ActionResult result)
        {
            switch (result.Status)
            {
                case StatusCode.Success:
                    _actionWriter.WriteSuccess($"{result.ActionType}: {result.ActionName} ... {result.Status}");
                    break;
                case StatusCode.Error:
                    _actionWriter.WriteError($"{result.ActionType}: {result.ActionName} ... {result.Status}");
                    break;
                case StatusCode.Timeout:
                    _actionWriter.WriteError($"{result.ActionType}: {result.ActionName} ... {result.Status}");
                    break;
                default:
                    _actionWriter.WriteInformation($"{result.ActionType}: {result.ActionName} ... {result.Status}");
                    break;
            }

            foreach (string message in result.Messages)
            {
                _actionWriter.WriteInformation(message);
            }

            _actionWriter.WriteInformation(string.Empty);
        }
    }
}
