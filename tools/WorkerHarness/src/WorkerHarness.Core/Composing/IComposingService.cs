using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorkerHarness.Core
{
    /// <summary>
    /// provide an abstraction for a composing service
    /// </summary>
    public interface IComposingService
    {
        /// <summary>
        /// Generate an ExecuationContext object that capture valid scenario paths and the number of execution repetitions
        /// </summary>
        /// <param name="composeFile">the absolute path to the compose.scenario file</param>
        /// <returns cref="IEnumerable{ScenarioInstruction}"></returns>
        Compose Compose(string composeFile);
    }
}
