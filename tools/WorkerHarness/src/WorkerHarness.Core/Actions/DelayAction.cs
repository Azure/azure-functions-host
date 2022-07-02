
using Microsoft.Extensions.Logging;

namespace WorkerHarness.Core
{
    internal class DelayAction : IAction
    {
        private readonly int _milisecondsDelay;

        private readonly ILogger<DelayAction> _logger;

        internal DelayAction(int milisecondsDelay, ILogger<DelayAction> logger)
        {
            if (_milisecondsDelay < -1)
            {
                throw new ArgumentOutOfRangeException($"Cannot except delay time less than -1");
            }
            _milisecondsDelay = milisecondsDelay;
            _logger = logger;
        }

        public async Task ExecuteAsync()
        {
            await Task.Delay(_milisecondsDelay);

            _logger.LogInformation("delay for {0} miliseconds", _milisecondsDelay);

        }
    }
}
