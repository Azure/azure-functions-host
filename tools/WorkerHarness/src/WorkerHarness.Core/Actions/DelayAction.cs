// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;

namespace WorkerHarness.Core.Actions
{
    internal sealed class DelayAction : IAction
    {
        private readonly ILogger<DelayAction> _logger;
        
        internal const string InvalidArgumentMessage = "Cannot except delay time less than -1";
        internal int DelayInMilliseconds { get; }

        internal DelayAction(int millisecondsDelay, ILogger<DelayAction> logger)
        {
            if (millisecondsDelay < -1)
            {
                throw new ArgumentException(InvalidArgumentMessage);
            }
            DelayInMilliseconds = millisecondsDelay;
            _logger = logger;
        }

        public async Task<ActionResult> ExecuteAsync(ExecutionContext executionContext)
        {
            _logger.LogInformation("Delay for {0} milliseconds", DelayInMilliseconds);

            await Task.Delay(DelayInMilliseconds);

            return new ActionResult() { Status = StatusCode.Success };
        }
    }
}
