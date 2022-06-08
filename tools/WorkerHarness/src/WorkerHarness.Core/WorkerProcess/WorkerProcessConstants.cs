using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorkerHarness.Core.WorkerProcess
{
    internal class WorkerProcessConstants
    {
        internal const string DefaultHostUri = "127.0.0.1";

        internal const int DefaultPort = 30052;

        internal const int GrpcMaxMessageLength = int.MaxValue;
    }
}
