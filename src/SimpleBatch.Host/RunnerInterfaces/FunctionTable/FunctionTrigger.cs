using System;

namespace RunnerInterfaces
{
    // Describes how a function can get triggered.
    // This is orthogonal to the binding.
    internal class FunctionTrigger
    {
        // If HasValue, then specify the function is invoked on the timer.
        public TimeSpan? TimerInterval { get; set; }

        // True if invocation should use a blob listener.
        public bool ListenOnBlobs { get; set; }
    }
}