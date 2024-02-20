// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WorkerHarness.Core.Parsing;
using WorkerHarness.Core.Profiling;

namespace WorkerHarness.Core.Actions
{
    internal class ImportAction : IAction
    {
        internal string Type => ActionTypes.Import;
        internal string ScenarioFile { get; }

        internal ImportAction(string scenarioFile)
        {
            ScenarioFile = scenarioFile;
        }

        public async Task<ActionResult> ExecuteAsync(ExecutionContext executionContext)
        {
            ArgumentNullException.ThrowIfNull(executionContext, nameof(executionContext));

            ActionResult importActionResult = new();

            Scenario scenario = executionContext.ScenarioParser.Parse(ScenarioFile);

            foreach (IAction action in scenario.Actions)
            {
                if (action is RpcAction { WaitForUserInput: true } rpcAction)
                {
                    Console.WriteLine($"Press any key to continue executing the next action ({rpcAction.Name})");
                    Console.ReadKey();
                }

                if (action is ICanStartProfiling { StartProfiling: true })
                {
                    if (executionContext?.Profiler != null)
                    {
                        await executionContext.Profiler.StartProfilingAsync();
                    }
                }

                ActionResult actionResult = await action.ExecuteAsync(executionContext!);

                if (action is ICanStopProfiling { StopProfiling: true } && executionContext?.Profiler != null)
                {
                    await executionContext.Profiler.StopProfilingAsync();
                }

                if (actionResult.Status is StatusCode.Failure)
                {
                    importActionResult.Status = StatusCode.Failure;

                    if (!executionContext!.ContinueUponFailure)
                    {
                        break;
                    }
                }
            }


            return importActionResult;
        }
    }
}