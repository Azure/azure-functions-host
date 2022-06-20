using System.Text.Json.Nodes;

namespace WorkerHarness.Core
{
    /// <summary>
    /// Parse a json scenario file into a Scenario object
    /// </summary>
    public class ScenarioParser : IScenarioParser
    {
        // TODO: list of action providers
        private IEnumerable<IActionProvider> _actionProviders;

        public ScenarioParser(IEnumerable<IActionProvider> actionProviders)
        {
            _actionProviders = actionProviders;
        }

        /// <summary>
        /// Parse a json scenario file. Use an IActionProvider object to create the right IAction object.
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
            JsonNode scenarioNode = JsonNode.Parse(scenarioInput) ?? throw new InvalidOperationException($"Unable to convert the {scenarioFile} file into a {typeof(JsonNode)} object");
            
            ValidateScenario(scenarioNode);

            string scenarioName = scenarioNode["scenarioName"]!.GetValue<string>();
            Scenario scenario = new(scenarioName);

            JsonArray jsonActions = scenarioNode["actions"]!.AsArray();
            for (int i = 0; i < jsonActions.Count; i++)
            {
                JsonNode actionNode = jsonActions[i]!;
                
                // find the type of action 
                if (actionNode["type"] == null)
                {
                    throw new MissingFieldException("Missing an action type in the scenario file");
                }
                string actionType = actionNode["type"]!.GetValue<string>();

                // select action provider based on the type of action
                var actionProvider = _actionProviders.FirstOrDefault(p => string.Equals(p.Type, actionType, StringComparison.OrdinalIgnoreCase));
                if (actionProvider == null)
                {
                    throw new InvalidDataException($"There is no action of type {actionType}");
                }

                // the the action provider to create the appropriate action node
                scenario.Actions.Add(actionProvider.Create(actionNode));
            }

            return scenario;
        }


        private void ValidateScenario(JsonNode scenarioNode)
        {
            if (scenarioNode["scenarioName"] == null)
            {
                throw new MissingFieldException($"Missing the 'scenarioName' property in the scenario file.");
            }

            if (scenarioNode["actions"] == null || scenarioNode["actions"] is not JsonArray)
            {
                throw new MissingFieldException($"Missing the 'actions' array in the scenario file");
            }
        }

    }
}
