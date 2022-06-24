using System.Text.Json.Nodes;

namespace WorkerHarness.Core
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

        /// <summary>
        /// Parse a scenario file with an Instruction object. 
        /// An Instruction object contains the scenario file and the repeat number.
        /// Use an IActionProvider object to create the right IAction object.
        /// </summary>
        /// <param name="scenarioContext" cref="Instruction"></param>
        /// <returns></returns>
        /// <exception cref="FileNotFoundException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="MissingFieldException"></exception>
        /// <exception cref="InvalidDataException"></exception>
        public Scenario Parse(Instruction scenarioContext)
        {
            string scenarioFile = scenarioContext.Action;

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

                // determine if action is repeatable
                bool actionRepeatable = actionNode["repeatable"] != null && actionNode["repeatable"]!.GetValue<bool>();

                // if the action is repeatable, repeat it x number of times, where x = scenarioContext.Repeat
                int repeats = actionRepeatable ? scenarioContext.Repeat : 1;

                // the the action provider to create the appropriate action node
                for (int _ = 0; _ < repeats; _++)
                {
                    scenario.Actions.Add(actionProvider.Create(actionNode));
                }
            }

            return scenario;
        }

        private static void ValidateScenario(JsonNode scenarioNode)
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
