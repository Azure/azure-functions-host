using System;

namespace Microsoft.Azure.Jobs.Perf
{
    internal class TimeBlock
    {
        private readonly DateTime _startTime = DateTime.Now;

        private bool _done = false;

        public TimeSpan ElapsedTime { get; set; }

        public void End()
        {
            if (_done)
            {
                throw new InvalidOperationException("End already called.");
            }

            _done = true;

            ElapsedTime = DateTime.Now - _startTime;
        }
    }
}
