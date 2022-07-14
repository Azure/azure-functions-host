// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core.Actions
{
    internal class DelayAction : IAction
    {
        private readonly int _milisecondsDelay;

        internal static string InvalidArgumentMessage = "Cannot except delay time less than -1";

        internal int MilisecondsDelay => _milisecondsDelay;

        internal DelayAction(int milisecondsDelay)
        {
            if (milisecondsDelay < -1)
            {
                throw new ArgumentException(InvalidArgumentMessage);
            }
            _milisecondsDelay = milisecondsDelay;
        }

        public async Task<ActionResult> ExecuteAsync(ExecutionContext executionContext)
        {
            await Task.Delay(_milisecondsDelay);

            ActionResult actionResult = new()
            {
                Status = StatusCode.Success,
                Message = $"delay for {_milisecondsDelay} miliseconds"
            };

            return actionResult;
        }
    }
}
