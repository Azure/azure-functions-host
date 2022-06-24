using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorkerHarness.Core
{
    /// <summary>
    /// an interface that is responsible to create a Scenario from a file
    /// </summary>
    public interface IScenarioComposer
    {
        /// <summary>
        /// Create a Scenario object from a compose file
        /// </summary>
        /// <param name="composeFile"></param>
        /// <returns cref="Scenario"></returns>
        public Scenario Compose(string composeFile);
    }
}
