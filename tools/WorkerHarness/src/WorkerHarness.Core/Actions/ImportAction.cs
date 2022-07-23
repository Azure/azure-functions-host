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
        private readonly ILogger<ImportAction> _logger;

        internal ImportAction(string scenarioFile,
            ILogger<ImportAction> logger)
        {
            _scenarioFile = scenarioFile;
            _logger = logger;
        }

        public async Task<ActionResult> ExecuteAsync(ExecutionContext execuationContext)
        {
            ActionResult importActionResult = new();
            
            IScenarioParser scenarioParser = execuationContext.ScenarioParser;
            Scenario scenario = scenarioParser.Parse(_scenarioFile);

            foreach (IAction action in scenario.Actions)
            {
                ActionResult actionResult = await action.ExecuteAsync(execuationContext);

                if (actionResult.Status is StatusCode.Failure)
                {
                    importActionResult.Status = StatusCode.Failure;

                    if (!execuationContext.ContinueUponFailure)
                    {
                        break;
                    }
                }
            }

            return importActionResult;
        }
    }
}
