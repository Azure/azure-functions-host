using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorkerHarness.Core
{
    /// <summary>
    /// Encapsulates information about a scenario
    /// </summary>
    public class Scenario
    {
        // name of a scenario file
        public string ScenarioName { get; private set; }

        // a list of actions to execute
        public IList<IAction> Actions { get; private set; }

        // continue to execute the remaining actions upon an action error
        public bool ContinueUponFailure { get; set; } = true;

        public Scenario(string scenarioName)
        {
            ScenarioName = scenarioName;
            Actions = new List<IAction>();
        }
    }
}
