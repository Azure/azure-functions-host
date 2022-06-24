using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorkerHarness.Core
{
    /// <summary>
    /// Capture the content of the compose file
    /// </summary>
    public class Compose
    {
        // a list of Instruction objects
        public IEnumerable<Instruction> Instructions { get; set; } = new List<Instruction>();

        // indicate whether to continue executing upon an error
        public bool ContinueUponFailure { get; set; } = true;

        // name of the scenario
        public string ScenarioName { get; set; } = string.Empty;
    }
}
