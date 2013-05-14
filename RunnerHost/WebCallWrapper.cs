using System;
using System.Collections.Generic;
using SimpleBatch;
using SimpleBatch.Client;
using System.Linq;

namespace RunnerHost
{
    // Adapter to expose ICall from a WebFunctionInvoker
    // Have this because WebFunctionInvoker is in a different assembly.
    class WebCallWrapper : ICall, ISelfWatch
    {
        private readonly WebFunctionInvoker _inner;

        public WebCallWrapper(WebFunctionInvoker inner)
        {
            _inner = inner;
        }

        public string GetStatus()
        {
            return _inner.GetStatus();
        }

        public IFunctionToken QueueCall(string functionName, object arguments = null, IEnumerable<IFunctionToken> prereqs = null)
        {
            var guid = _inner.QueueCall(functionName, arguments, CallUtil.Unwrap(prereqs));
            return new SimpleFunctionToken(guid);
        }
    }

    public class CallUtil
    {
        public static IEnumerable<Guid> Unwrap(IEnumerable<IFunctionToken> prereqs)
        {
            if (prereqs == null)
            {
                return null;
            }
            var prereqs2 = from prereq in prereqs select prereq.Guid;
            return prereqs2;
        }

        // Pass parent guid through parameters as special keyname.
        const string FunctionInstanceGuidKeyName = "$this";

        public static void AddFunctionGuid(Guid thisFunc, IDictionary<string, string> args)
        {
            args[FunctionInstanceGuidKeyName] = thisFunc.ToString();
        }

        public static Guid GetParentGuid(IDictionary<string, string> args)
        {
            if (args != null)
            {
                string guidAsString;
                if (args.TryGetValue(FunctionInstanceGuidKeyName, out guidAsString))
                {
                    return Guid.Parse(guidAsString);
                }
            }
            return Guid.Empty;
        }
    }
}
