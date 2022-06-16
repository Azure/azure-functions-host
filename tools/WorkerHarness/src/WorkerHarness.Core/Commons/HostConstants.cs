using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorkerHarness.Core
{
    public class HostConstants
    {
        public const string DefaultHostUri = "127.0.0.1";

        public const int DefaultPort = 30052;

        public const int GrpcMaxMessageLength = int.MaxValue;

        public const string HostVersion = "4.3.2.0";
    }
}
