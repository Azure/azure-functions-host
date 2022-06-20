using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorkerHarness.Core
{
    /// <summary>
    /// Encapsulates information about a Scenario.json file
    /// </summary>
    public class Scenario
    {
        // name of a scenario file
        public string ScenarioName { get; private set; }

        // a list of actions to execute
        public IList<IAction> Actions { get; private set; }

        public Scenario(string scenarioName)
        {
            ScenarioName = scenarioName;
            Actions = new List<IAction>();
        }
    }
}
