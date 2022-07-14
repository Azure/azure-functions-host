// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json;
using System.Text.Json.Nodes;
using WorkerHarness.Core.Actions;

namespace WorkerHarness.Core.Parsing
{
    /// <summary>
    /// Parse a json scenario file into a Scenario object
    /// </summary>
    public class ScenarioParser : IScenarioParser
    {
        private readonly IEnumerable<IActionProvider> _actionProviders;

        // Exception messages
        internal static string ScenarioFileNotFoundMessage = "The scenario file {0} is not found";
        internal static string ScenarioFileNotInJsonFormat = "ScenarioParser exception occurs when parsing {0}. {1}";
        internal static string ScenarioFileMissingActionsList = "Missing the 'actions' array in the scenario {0}";
        internal static string ScenarioFileMissingActionType = "Mising the \"actionType\" property in an action {0}";
        internal static string ScenarioFileHasInvalidActionType = "Worker Harness does not recognize the action type: {0}";

        public ScenarioParser(IEnumerable<IActionProvider> actionProviders)
        {
            _actionProviders = actionProviders;
        }

        /// <summary>
        /// Parse a scenario file. Use an IActionProvider object to create the right IAction object.
        /// </summary>
        /// <param name="scenarioFile"></param>
        /// <returns></returns>
        /// <exception cref="FileNotFoundException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="MissingFieldException"></exception>
        public Scenario Parse(string scenarioFile)
        {
            if (!File.Exists(scenarioFile))
            {
                throw new FileNotFoundException(string.Format(ScenarioFileNotFoundMessage, scenarioFile));
            }

            // Read the json file and convert it to a JsonNode object for parsing
            JsonNode scenarioNode;
            try
            {
                string scenarioInput = File.ReadAllText(scenarioFile);
                scenarioNode = JsonNode.Parse(scenarioInput)!;
            }
            catch (Exception ex)
            {
                throw new ArgumentException(string.Format(ScenarioFileNotInJsonFormat, scenarioFile, ex.Message));
            }

            ValidateScenario(scenarioNode, scenarioFile);

            string scenarioName = scenarioNode["scenarioName"]?.GetValue<string>() ?? Path.GetFileName(scenarioFile);
            Scenario scenario = new(scenarioName);

            IList<JsonNode> actions = scenarioNode["actions"]!.AsArray()!.ToList<JsonNode>();

            foreach (JsonNode actionNode in actions)
            {
                if (actionNode["actionType"] == null)
                {
                    string exceptionMessage = string.Format(ScenarioFileMissingActionType, JsonSerializer.Serialize(actionNode));
                    throw new ArgumentException(exceptionMessage);
                }

                string actionType = actionNode["actionType"]!.GetValue<string>();

                // select action provider based on the type of action
                var actionProvider = _actionProviders.FirstOrDefault(p => string.Equals(p.Type, actionType, StringComparison.OrdinalIgnoreCase));
                if (actionProvider == null)
                {
                    throw new ArgumentException(string.Format(ScenarioFileHasInvalidActionType, actionType));
                }

                // the the action provider to create the appropriate action node
                scenario.Actions.Add(actionProvider.Create(actionNode));
            }

            return scenario;
        }

        private static void ValidateScenario(JsonNode scenarioNode, string scenarioPath)
        {
            if (scenarioNode["actions"] == null || scenarioNode["actions"] is not JsonArray)
            {
                throw new ArgumentException(string.Format(ScenarioFileMissingActionsList, scenarioPath));
            }
        }

    }
}
