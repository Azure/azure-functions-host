using System;
using System.Collections.Generic;
using SimpleBatch;
using SimpleBatch.Client;

namespace RunnerHost
{
    // Adapter to expose interfaces on a web invoker.
    class WebCallWrapper : ICall, ISelfWatch
    {
        private readonly FunctionInvoker _inner;
        private readonly Guid _thisFunc;

        public WebCallWrapper(FunctionInvoker inner, Guid thisFunc)
        {
            _inner = inner;
            _thisFunc = thisFunc;
        }

        public string GetStatus()
        {
            return _inner.GetStatus();
        }

        public IFunctionToken QueueCall(string functionName, object arguments = null, IEnumerable<IFunctionToken> prereqs = null)
        {
            IEnumerable<Guid> prereqGuids = NormalizePrereqs(prereqs);
            var args = ResolveArgs(arguments);

            var guid = _inner.QueueCall(functionName, args, prereqGuids);
            return new SimpleFunctionToken(guid);
        }
                
        private IDictionary<string, string> ResolveArgs(object arguments)
        {
           var d = (arguments == null) ? 
               new Dictionary<string,string>() :
               RunnerInterfaces.ObjectBinderHelpers.ConvertObjectToDict(arguments);
           d["$this"] = _thisFunc.ToString();
           return d;
        }

        private IEnumerable<Guid> NormalizePrereqs(IEnumerable<IFunctionToken> prereqs)
        {
            // Current function is a prereq. This means queued functions don't execute
            // until the current function is done. 
            if (_thisFunc != Guid.Empty)
            {
                yield return _thisFunc;
            }

            if (prereqs != null)
            {
                foreach (var token in prereqs)
                {
                    yield return ((SimpleFunctionToken) token).Guid;
                }
            }
        }
    }

    class SimpleFunctionToken : IFunctionToken
    {
        public SimpleFunctionToken(Guid g)
        {
            this.Guid = g;
        }

        public Guid Guid { get; private set; }
    }
}
