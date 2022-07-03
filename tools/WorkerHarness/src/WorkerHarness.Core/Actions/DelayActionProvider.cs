// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json.Nodes;

namespace WorkerHarness.Core
{
    public class DelayActionProvider : IActionProvider
    {
        public string Type => ActionTypes.Delay;

        public IAction Create(JsonNode actionNode)
        {
            var delayTime = actionNode["delay"]?.GetValue<int>() ?? 0;

            return new DelayAction(delayTime);
        }

    }
}
