// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core
{
    internal class DelayAction : IAction
    {
        private readonly int _milisecondsDelay;

        internal DelayAction(int milisecondsDelay)
        {
            if (_milisecondsDelay < -1)
            {
                throw new ArgumentOutOfRangeException($"Cannot except delay time less than -1");
            }
            _milisecondsDelay = milisecondsDelay;
        }

        public async Task<ActionResult> ExecuteAsync()
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
