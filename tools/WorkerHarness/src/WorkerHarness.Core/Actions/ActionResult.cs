using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorkerHarness.Core
{
    public enum StatusCode
    {
        Success,
        Error,
        Timeout
    }

    public class ActionResult
    {
        public string ActionType { get; set; }

        public string ActionName { get; set; }

        public StatusCode Status { get; set; }

        public IList<string> Messages { get; set; }

        public ActionResult(string actionType, string actionName)
        {
            ActionType = actionType;
            ActionName = actionName;
            Messages = new List<string>();
        }
    }
}
