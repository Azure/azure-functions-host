// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace WorkerHarness.Core.Parsing
{
    /// <summary>
    /// Parse a json scenario file into a Scenario object
    /// </summary>
    public class ScenarioParser : IScenarioParser
    {
        private readonly IEnumerable<IActionProvider> _actionProviders;

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
                throw new FileNotFoundException($"The scenario file {scenarioFile} is not found");
            }

            // Read the json file and convert it to a JsonNode object for parsing
            string scenarioInput = File.ReadAllText(scenarioFile);
            JsonNode scenarioNode = JsonNode.Parse(scenarioInput) ?? throw new JsonException($"{scenarioFile} does not represent a valid single JSON value");

            ValidateScenario(scenarioNode, scenarioFile);

            string scenarioName = scenarioNode["scenarioName"]?.GetValue<string>() ?? Path.GetFileName(scenarioFile);
            Scenario scenario = new(scenarioName);

            IList<JsonNode> actions = ResolveImportInActions(scenarioNode["actions"]!.AsArray());

            foreach (JsonNode actionNode in actions)
            {
                if (actionNode["actionType"] == null)
                {
                    throw new MissingFieldException($"Missing the \"actionType\" property in {JsonSerializer.Serialize(actionNode)} after resolving imports");
                }

                string actionType = actionNode["actionType"]!.GetValue<string>();

                // select action provider based on the type of action
                var actionProvider = _actionProviders.FirstOrDefault(p => string.Equals(p.Type, actionType, StringComparison.OrdinalIgnoreCase));
                if (actionProvider == null)
                {
                    throw new InvalidDataException($"There is no action of type \"{actionType}\"");
                }

                // the the action provider to create the appropriate action node
                scenario.Actions.Add(actionProvider.Create(actionNode));
            }

            return scenario;
        }

        /// <summary>
        /// Import actions from the scenario file specified in the "import" property in each action node
        /// </summary>
        /// <param name="jsonActions" cref="JsonArray"></param>
        /// <returns></returns>
        private static IList<JsonNode> ResolveImportInActions(JsonArray jsonActions)
        {
            IList<JsonNode> actionsBuffer = new List<JsonNode>();

            for (int i = 0; i < jsonActions.Count; i++)
            {
                JsonNode actionNode = jsonActions[i] 
                    ?? throw new NullReferenceException($"The element at index {i} of an {typeof(JsonArray)} is null. Expect a {typeof(JsonNode)} type");

                if (actionNode["import"] != null && actionNode["import"]!.GetValue<string>() is string filePath)
                {
                    AddActions(filePath, ref actionsBuffer);
                }
                else
                {
                    actionsBuffer.Add(actionNode);
                }
            }

            jsonActions.Clear();

            return actionsBuffer;
        }

        private static void AddActions(string filePath, ref IList<JsonNode> actionsBuffer)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"{filePath} is not found");
            }

            // read the file and convert it to a JsonNode
            string fileInput = File.ReadAllText(filePath);
            JsonNode fileAsJsonNode = JsonNode.Parse(fileInput) ?? throw new JsonException($"{filePath} does not represent a valid single JSON value");

            // validate that the file is a scenario file
            ValidateScenario(fileAsJsonNode, filePath);

            // read all actions in the file and add them to the actionsBuffer list
            JsonArray fileActions = fileAsJsonNode["actions"]!.AsArray();
            for (int i = 0; i < fileActions.Count; i++)
            {
                JsonNode actionNode = fileActions[i] ?? throw new NullReferenceException($"The element at index {i} of an {typeof(JsonArray)} is null. Expect a {typeof(JsonNode)} type");
                actionsBuffer.Add(actionNode);
            }

            fileActions.Clear(); // remove all actionNodes from their parent "fileActions" in this case
        }

        private static void ValidateScenario(JsonNode scenarioNode, string scenarioPath)
        {
            if (scenarioNode["actions"] == null || scenarioNode["actions"] is not JsonArray)
            {
                throw new MissingFieldException($"Missing the 'actions' array in the scenario {scenarioPath}");
            }
        }

    }
}
