using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorkerHarness.Core
{
    /// <summary>
    /// Encapsulate information about an action
    /// </summary>
    internal class DefaultActionData
    {
        // the type of an action
        public string? Type { get; set; }

        // the name of an action
        public string? Name { get; set; }

        // the amount of time to execute an action
        public int Timeout { get; set; }

        // a list of incoming messages
        public IList<IncomingMessage> IncomingMessages { get; private set; }

        // a list of outgoing messages
        public IList<OutgoingMessage> OutgoingMessages { get; private set; }

        public DefaultActionData()
        {
            Timeout = 10000;
            IncomingMessages = new List<IncomingMessage>();
            OutgoingMessages = new List<OutgoingMessage>();
        }
    }
}
