// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;
using WorkerHarness.Core.Parsing;

namespace WorkerHarness.Core.Actions
{
    internal class ImportAction : IAction
    {
        internal string Type => ActionTypes.Import;
        internal string ScenarioFile => _scenarioFile;

        internal static string ExceptionMessage = "An exception occurs inside ImportAction.";

        private readonly string _scenarioFile;

        internal ImportAction(string scenarioFile)
        {
            _scenarioFile = scenarioFile;
        }

        public async Task<ActionResult> ExecuteAsync(ExecutionContext executionContext)
        {
            ActionResult importActionResult = new();

            IScenarioParser scenarioParser = executionContext.ScenarioParser;
            Scenario scenario = scenarioParser.Parse(_scenarioFile);

            foreach (IAction action in scenario.Actions)
            {
                if (action is RpcAction rpcAction)
                {
                    if (rpcAction.WaitForUserInput)
                    {
                        Console.WriteLine($"Press any key to continue executing the next action ({rpcAction.Name})");
                        Console.ReadKey();
                    }
                }

                ActionResult actionResult = await action.ExecuteAsync(executionContext);

                if (actionResult.Status is StatusCode.Failure)
                {
                    importActionResult.Status = StatusCode.Failure;

                    if (!executionContext.ContinueUponFailure)
                    {
                        break;
                    }
                }
            }

            return importActionResult;
        }
    }
}
