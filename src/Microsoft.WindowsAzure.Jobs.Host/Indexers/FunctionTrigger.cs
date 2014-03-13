using System;

namespace Microsoft.WindowsAzure.Jobs
{
    // Describes how a function can get triggered.
    // This is orthogonal to the binding.
    internal class FunctionTrigger
    {
        // True if invocation should use a blob listener.
        public bool ListenOnBlobs { get; set; }
    }
}
