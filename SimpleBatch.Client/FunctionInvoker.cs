using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SimpleBatch.Client
{
    public abstract class FunctionInvoker
    {
        // $$$ Where would we get these from?
        public IDictionary<string, string> InheritedArgs { get; set; }

        // Implements ICall // !!! Rationalize?
        // Defers calls to avoid races.
        public Guid QueueCall(string functionShortName, object arguments = null, IEnumerable<Guid> prereqs = null)
        {
            _countQueued++;
            var args = ResolveArgs(arguments);

            var guid = this.InvokeDirect(functionShortName, args, prereqs);
            return guid;
        }       

        private IEnumerable<Guid> NormalizePrereqs(IEnumerable<Guid> prereqs)
        {
            if (prereqs == null)
            {
                return new Guid[0];
            }
            return prereqs;                
        }

        // Invokes, queues an execution. 
        // Function could start running immediately. 
        protected Guid InvokeDirect(string functionShortName, IDictionary<string, string> args, IEnumerable<Guid> prereqs = null)
        {
            Guid guid = MakeWebCall(functionShortName, args, NormalizePrereqs(prereqs));

            return guid;
        }

        // Synchronous invoke. Blocks until function has finished executing. 
        // To make this async, we'd need to return a (Task, Guid) combo. 
        // Can't return Task<Guid> since the guid is available before the task is complete.
        public Guid Invoke(string functionShortName, object arguments = null)
        {
            var args = ResolveArgs(arguments);

            // Function is queued, Guid is available immediately. 
            Guid g = InvokeDirect(functionShortName, args);

            // Now wait.
            WaitOnCall(g).Wait();
            
            return g;
        }

        // Arguments is either null (nothing), an IDict, or an object whose properties are the arguments. 
        protected IDictionary<string, string> ResolveArgs(object arguments)
        {
            var args = new Dictionary<string, string>();

            // Start with inhereted, and then overwrite with any explicit arguments.
            if (InheritedArgs != null)
            {
                foreach (var kv in InheritedArgs)
                {
                    args[kv.Key] = kv.Value;
                }
            }

            // !!! Double copy?
            if (arguments != null)
            {
                var d = ObjectBinderHelpers.ConvertObjectToDict(arguments);
                foreach (var kv in d)
                {
                    args[kv.Key] = kv.Value;
                }
            }

            return args;
        }

        volatile int _countQueued;

        public string GetStatus()
        {
            return string.Format("Queued {0} calls", _countQueued);
        }

        protected abstract Guid MakeWebCall(string functionShortName, IDictionary<string, string> parameters, IEnumerable<Guid> prereqs);

        protected abstract Task WaitOnCall(Guid g);
    }
}