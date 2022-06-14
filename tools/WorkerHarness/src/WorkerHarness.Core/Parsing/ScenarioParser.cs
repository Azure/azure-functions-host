using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace WorkerHarness.Core
{
    public class ScenarioParser : IScenarioParser
    {
        private IActionProvider _actionProvider;

        public ScenarioParser(IActionProvider actionProvider)
        {
            _actionProvider = actionProvider;
        }

        public Scenario Parse(string scenarioFile)
        {
            if (!File.Exists(scenarioFile))
            {
                throw new FileNotFoundException($"The scenario file {scenarioFile} is not found");
            }

            string scenarioInput = File.ReadAllText(scenarioFile);
            JsonNode scenarioNode = JsonNode.Parse(scenarioInput)!;
            string? scenarioName = scenarioNode["scenarioName"]!.GetValue<string>();
            Scenario scenario = new Scenario(scenarioName);

            JsonArray jsonActions = scenarioNode["actions"]!.AsArray();
            for (int i = 0; i < jsonActions.Count; i++)
            {
                JsonNode actionNode = jsonActions[i]!;
                scenario.Actions.Add(_actionProvider.Create(actionNode));
            }

            return scenario;
        }

    }
}
