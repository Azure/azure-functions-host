using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace WorkerHarness.Core
{
    public class DelayActionProvider : IActionProvider
    {
        public string Type => ActionType.Delay;

        public IAction Create(JsonNode actionNode)
        {
            var delayTime = actionNode["delay"]?.GetValue<int>() ?? 0;

            return new DelayAction(delayTime);
        }

        public IAction Create(IDictionary<string, string> context)
        {
            throw new NotImplementedException();
        }
    }
}
