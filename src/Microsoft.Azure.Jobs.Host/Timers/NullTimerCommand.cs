using System;
using System.Threading;

namespace Microsoft.Azure.Jobs
{
    internal class NullTimerCommand : IIntervalSeparationCommand
    {
        public TimeSpan SeparationInterval
        {
            get { return Timeout.InfiniteTimeSpan; }
        }

        public void Execute()
        {
        }
    }
}
