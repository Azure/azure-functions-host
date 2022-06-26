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
    internal class RpcActionData
    {
        // the type of an action
        public string ActionType { get; } = ActionTypes.Rpc;

        // the name of an action
        public string ActionName { get; set; } = string.Empty;

        // the amount of time to execute an action; default to 5s timeout
        public int Timeout { get; set; } = 5000;

        public IEnumerable<RpcActionMessage> Messages { get; set; } = new List<RpcActionMessage>();

        //// a list of incoming messages
        //public IList<IncomingMessage> IncomingMessages { get; private set; } = new List<IncomingMessage>();

        //// a list of outgoing messages
        //public IList<OutgoingMessage> OutgoingMessages { get; private set; } = new List<OutgoingMessage>();


    }
}
