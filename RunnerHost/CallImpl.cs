using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RunnerInterfaces;
using SimpleBatch;
using SimpleBatch.Client;

namespace RunnerHost
{
    // Adapter to expose interfaces on a web invoker.
    class WebCallWrapper : ICall, ISelfWatch
    {
        public FunctionInvoker _inner;

        public string GetStatus()
        {
            return _inner.GetStatus();
        }

        public IFunctionToken QueueCall(string functionName, object arguments = null, IEnumerable<IFunctionToken> prereqs = null)
        {
            return _inner.QueueCall(functionName, arguments, prereqs);
        }
    }
}
