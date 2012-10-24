using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SimpleBatch;

namespace RunnerHost
{
#if false
    !!! Don't assume we have a URL antares site running.
        The site just queus anyways
        But site queues an Execution message. So service is simpler.
    *** How do we get URL?

    Can we queue directly ourselves?
        How do we get the Queue?
#endif

    class CallImpl : ICall
    {
        const string defaultUri = @"http://daas2.azurewebsites.net/";

        string _serviceUri;

        public CallImpl(string serviceUri = null)
        {
            _serviceUri = (serviceUri == null) ? defaultUri : serviceUri;            
        }

        // 
        public IDictionary<string, string> InheritedArgs { get; set; }

        // Queues up. 
        public Guid InvokeAsync(string functionName, object arguments = null)
        {
            var func = ResolveFunction(functionName);

            var args = new Dictionary<string, string>();

            // Start with inhereted, and then overwrite with any explicit arguments.
            foreach (var kv in InheritedArgs)
            {
                args[kv.Key] = kv.Value;
            }

            if (arguments != null)
            {
                var d = RunnerInterfaces.ObjectBinderHelpers.ConvertObjectToDict(arguments);
                foreach (var kv in d)
                {
                    args[kv.Key] = kv.Value;
                }   
            }


        }

        // May convert a shortname to a fully qualified name that we can invoke.
        string ResolveFunction(string functionName)
        {
            return functionName;
        }

        private string MakeUri(string function, object parameters)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(_serviceUri);
            sb.Append("api/run/");
            sb.AppendFormat("?func={0}", function);

            foreach (var kv in ConvertObjectToDict(parameters))
            {
                sb.AppendFormat("&{0}={1}", kv.Key, kv.Value);
            }
            return sb.ToString();
        }


        // Drain the queue, send the messages.
        public void Flush()
        {
        }
    }
}
