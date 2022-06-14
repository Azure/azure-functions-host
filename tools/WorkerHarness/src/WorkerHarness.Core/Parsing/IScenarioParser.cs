using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorkerHarness.Core
{
    public interface IScenarioParser
    {
        Scenario Parse(string scenarioFile);
    }
}
