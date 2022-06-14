using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorkerHarness.Core
{
    public class Scenario
    {
        public string? ScenarioName { get; private set; }

        public IList<IAction> Actions { get; private set; }

        public Scenario(string scenarioName)
        {
            ScenarioName = scenarioName;
            Actions = new List<IAction>();
        }
    }
}
