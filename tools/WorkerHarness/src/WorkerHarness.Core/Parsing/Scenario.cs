// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using WorkerHarness.Core.Actions;

namespace WorkerHarness.Core.Parsing
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

        public Scenario(string scenarioName)
        {
            ScenarioName = scenarioName;
            Actions = new List<IAction>();
        }
    }
}
