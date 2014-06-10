using System;

namespace Microsoft.Azure.Jobs
{
    internal class TriggerReason
    {
        public Guid ParentGuid { get; set; }
        public Guid ChildGuid { get; set; }
        public string Message { get; set; }
    }
}
