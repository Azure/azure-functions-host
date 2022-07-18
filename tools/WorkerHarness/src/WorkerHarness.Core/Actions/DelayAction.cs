// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;

namespace WorkerHarness.Core.Actions
{
    internal class DelayAction : IAction
    {
        private readonly int _milisecondsDelay;
        private readonly ILogger<DelayAction> _logger;

        internal static string InvalidArgumentMessage = "Cannot except delay time less than -1";

        internal int MilisecondsDelay => _milisecondsDelay;

        internal DelayAction(int milisecondsDelay, ILogger<DelayAction> logger)
        {
            if (milisecondsDelay < -1)
            {
                throw new ArgumentException(InvalidArgumentMessage);
            }
            _milisecondsDelay = milisecondsDelay;
            _logger = logger;
        }

        public async Task<ActionResult> ExecuteAsync(ExecutionContext executionContext)
        {
            _logger.LogInformation("Delay for {0} miliseconds", _milisecondsDelay);

            await Task.Delay(_milisecondsDelay);

            ActionResult actionResult = new() { Status = StatusCode.Success };

            return actionResult;
        }
    }
}
