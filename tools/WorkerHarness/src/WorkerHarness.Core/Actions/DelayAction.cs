using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorkerHarness.Core
{
    internal class DelayAction : IAction
    {
        private int _milisecondsDelay;

        internal DelayAction(int milisecondsDelay)
        {
            if (_milisecondsDelay < -1)
            {
                throw new ArgumentOutOfRangeException($"Cannot except delay time less than -1");
            }
            _milisecondsDelay = milisecondsDelay;
        }

        public async Task ExecuteAsync()
        {
            Console.WriteLine($"Delaying for {_milisecondsDelay}");
            await Task.Delay(_milisecondsDelay);
        }
    }
}
