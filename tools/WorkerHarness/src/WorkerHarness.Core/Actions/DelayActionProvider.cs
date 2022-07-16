// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

namespace WorkerHarness.Core.Actions
{
    public class DelayActionProvider : IActionProvider
    {
        public string Type => ActionTypes.Delay;

        private readonly ILoggerFactory _loggerFactory;

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
