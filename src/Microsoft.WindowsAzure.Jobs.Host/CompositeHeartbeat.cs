using System;
using System.Collections.Generic;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class CompositeHeartbeat : IHeartbeat
    {
        private readonly IEnumerable<IHeartbeat> _heartbeats;

        public CompositeHeartbeat(params IHeartbeat[] heartbeats)
        {
            if (heartbeats == null)
            {
                throw new ArgumentNullException("heartbeats");
            }

            _heartbeats = heartbeats;
        }

        public void Beat()
        {
            foreach (IHeartbeat heartbeat in _heartbeats)
            {
                if (heartbeat == null)
                {
                    continue;
                }

                heartbeat.Beat();
            }
        }
    }
}
