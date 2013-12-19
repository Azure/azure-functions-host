using System;
using System.Collections.Generic;

namespace Microsoft.WindowsAzure.Jobs
{
    // Wraps another ICall, but chains a current function guid onto the prerequisites. 
    class CallWithContextWrapper : ICall, ISelfWatch
    {
        volatile int _count;
        volatile ICall _inner;

        private readonly Guid _thisFunc;

        public CallWithContextWrapper(ICall inner, Guid thisFunc)
        {
            _inner = inner;
            _thisFunc = thisFunc;
        }

        public IFunctionToken QueueCall(string functionName, object arguments = null, IEnumerable<IFunctionToken> prereqs = null)
        {
            _count++;
            var prereqs2 = NormalizePrereqs(prereqs);
            var args2 = ResolveArgs(arguments);

            return _inner.QueueCall(functionName, args2, prereqs2);
        }

        public string GetStatus()
        {
            return string.Format("Made {0} calls.", _count);
        }

        private IDictionary<string, string> ResolveArgs(object arguments)
        {
            var d = ObjectBinderHelpers.ConvertObjectToDict(arguments);
            CallUtil.AddFunctionGuid(_thisFunc, d);
            return d;
        }

        private IEnumerable<IFunctionToken> NormalizePrereqs(IEnumerable<IFunctionToken> prereqs)
        {
            // Current function is a prereq. This means queued functions don't execute
            // until the current function is done. 
            if (_thisFunc != Guid.Empty)
            {
                yield return new SimpleFunctionToken(_thisFunc);
            }

            if (prereqs != null)
            {
                foreach (var token in prereqs)
                {
                    yield return token;
                }
            }
        }
    }
}
