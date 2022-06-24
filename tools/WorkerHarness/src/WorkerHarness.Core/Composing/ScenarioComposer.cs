using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace WorkerHarness.Core
{
    internal class ScenarioComposer : IScenarioComposer
    {
        private readonly IEnumerable<IActionProvider> _actionProviders;

        public ScenarioComposer(IEnumerable<IActionProvider> actionProviders)
        {
            _actionProviders = actionProviders;
        }

        public Scenario Compose(string composeFile)
        {
            // map to a Compose object
            string fileInput = File.ReadAllText(composeFile);

            JsonSerializerOptions options = new()
            {
                PropertyNameCaseInsensitive = true
            };
            Compose composeObject = JsonSerializer.Deserialize<Compose>(fileInput, options) ?? 
                throw new InvalidOperationException($"Unable to deserialize the {composeFile} file to a Compose object");

            // create a scenario object
            string scenarioName = string.IsNullOrEmpty(composeObject.ScenarioName) ? Path.GetFileName(composeFile) : composeObject.ScenarioName;
            Scenario scenario = new(scenarioName)
            {
                ContinueUponFailure = composeObject.ContinueUponFailure
            };

            // for each instruction, create the appropriate action and add to scenario
            foreach (Instruction instruction in composeObject.Instructions)
            {
                IActionProvider? actionProvider = _actionProviders.FirstOrDefault(p => string.Equals(p.Type, instruction.Action, StringComparison.OrdinalIgnoreCase));
                if (actionProvider == null)
                {
                    throw new InvalidDataException($"Invalid action \"{instruction.Action}\" in the {composeFile} file");
                }

                //scenario.Actions.Add(actionProvider.Create());
            }
            throw new NotImplementedException();
        }
    }
}
