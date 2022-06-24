using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorkerHarness.Core
{
    /// <summary>
    /// Basic data that all types of actions share
    /// </summary>
    internal class BaseActionData
    {
        // the type of an action
        public string Type { get; set; } = string.Empty;

        // the name of an action
        public string Name { get; set; } = string.Empty;

        // the amount of time to execute an action; default to 10s timeout
        public int Timeout { get; set; } = 10000;
    }
}
