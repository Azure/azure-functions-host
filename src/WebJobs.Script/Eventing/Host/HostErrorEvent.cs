using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Azure.WebJobs.Script.Eventing
{
    public class HostErrorEvent : ScriptEvent
    {
        public HostErrorEvent(Exception e)
            : base(nameof(HostErrorEvent), EventSources.Worker)
        {
            Exception = e;
        }

        public Exception Exception { get; private set; }
    }
}
