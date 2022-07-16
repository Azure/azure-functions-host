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

            _logger.LogInformation("Executing the scenario {0}", scenario.ScenarioName);

            foreach (IAction action in scenario.Actions)
            {
                ActionResult actionResult = await action.ExecuteAsync(execuationContext);

                ShowActionResult(actionResult, execuationContext);
                    
                if (actionResult.Status is StatusCode.Failure)
                {
                    importActionResult.Status = StatusCode.Failure;
                }
            }

            string status = importActionResult.Status == StatusCode.Success ? "succeeds" : "fails";
            importActionResult.Message = $"The import action {status}";

            return importActionResult;
        }

        private void ShowActionResult(ActionResult actionResult, ExecutionContext executionContext)
        {
            switch (actionResult.Status)
            {
                case StatusCode.Success:
                    _logger.LogInformation(actionResult.Message);
                    break;

                case StatusCode.Failure:
                    _logger.LogError(actionResult.Message);

                    IEnumerable<string> messages = executionContext.DisplayVerboseError ? 
                        actionResult.VerboseErrorMessages : actionResult.ErrorMessages;

                    foreach (string message in messages)
                    {
                        _logger.LogError(message);
                    }

                    break;

                default:
                    break;
            }
        }
    }
}
