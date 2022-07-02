using Grpc.Core.Logging;
using Microsoft.Extensions.Logging;
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
        private readonly ILoggerFactory _loggerFactory;

        public string Type => ActionTypes.Delay;

        public DelayActionProvider(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
        }

        public IAction Create(JsonNode actionNode)
        {
            var delayTime = actionNode["delay"]?.GetValue<int>() ?? 0;

            return new DelayAction(delayTime, _loggerFactory.CreateLogger<DelayAction>());
        }

    }
}
