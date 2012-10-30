using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SimpleBatch;

namespace RunnerHost
{
    // Bind to ICall
    public class CallBinderProvider : ICloudBinderProvider 
    {
        class CallBinder : ICloudBinder
        {
            public BindResult Bind(IBinder bindingContext, System.Reflection.ParameterInfo parameter)
            {
                var result = new WebCallImpl(string.Empty); // $$$ Plumb through URL
                return new BindResultTransaction
                {
                    Result = result,
                    Cleanup = () =>
                        {
                            result.Flush();
                        }
                };
            }
        }
        public ICloudBinder TryGetBinder(Type targetType)
        {
            if (targetType == typeof(ICall))
            {
                return new CallBinder();
            }
            return null;
        }
    }
    

    public abstract class CallImpl : ICall, ISelfWatch
    {       
        // $$$ Where would we get these from?
        public IDictionary<string, string> InheritedArgs { get; set; }

        // Queues, for being flushed later
        List<Action> _queue = new List<Action>();
        
        // Implements ICall
        // Defers calls to avoid races.
        public void QueueCall(string functionShortName, object arguments = null)
        {
            var args = ResolveArgs(arguments);

            // $$$ Bug: if arguments is a mutable dict, need to copy it now.
            _queue.Add(()=> 
                {
                    this.InvokeDirect(functionShortName, args);
                });
        }

        // Drain the queue, send the messages.
        public void Flush()
        {
            var q = _queue;
            _queue = new List<Action>();

            foreach (Action a in q)
            {
                a(); // may add new queue messages
            }
        }

        // Invokes, queues an execution. 
        // Function could start running immediately. 
        protected Guid InvokeDirect(string functionShortName, IDictionary<string, string> args)
        {
            Guid guid = MakeWebCall(functionShortName, args);

            _count++;
            return guid;
        }

        // Invoke 
        public Task InvokeAsync(string functionShortName, object arguments = null)
        {
            var args = ResolveArgs(arguments);

            Guid g = InvokeDirect(functionShortName, args);

            // Now wait.
            return WaitOnCall(g);
        }

        public void Invoke(string functionShortName, object arguments = null)
        {
            Task t = InvokeAsync(functionShortName, arguments);
            t.Wait();
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

            if (arguments != null)
            {
                var d = RunnerInterfaces.ObjectBinderHelpers.ConvertObjectToDict(arguments);
                foreach (var kv in d)
                {
                    args[kv.Key] = kv.Value;
                }
            }

            return args;
        }

        volatile int _count;

        public string GetStatus()
        {
            return string.Format("Made {0} calls", _count);
        }

        protected abstract Guid MakeWebCall(string functionShortName, IDictionary<string, string> parameters);

        protected abstract Task WaitOnCall(Guid g);
            
    }

    // Invocation that calls out to a web service.
    class WebCallImpl : CallImpl
    {
        const string defaultUri = @"http://daas2.azurewebsites.net/";

        private readonly string _serviceUri;
        private readonly string _scope; // fully qualified cloud name, incluing storage container

        public WebCallImpl(string scope, string serviceUri = null)
        {
            _scope = scope;
            _serviceUri = (serviceUri == null) ? defaultUri : serviceUri;            
        }
                
        protected override Guid MakeWebCall(string functionShortName, IDictionary<string, string> parameters)
        {
            var function = ResolveFunction(functionShortName);                        
            string uri = MakeUri(function, parameters);

            // Send 
            WebRequest request = WebRequest.Create(uri);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.ContentLength = 0;
            
            var response = request.GetResponse(); // does the actual web request

            var stream2 = response.GetResponseStream();
            var text = new StreamReader(stream2).ReadToEnd();
            stream2.Close();

            BeginRunResult val = JsonConvert.DeserializeObject<BeginRunResult>(text);

            return val.Instance;
        }

        // May convert a shortname to a fully qualified name that we can invoke.
        // $$$ This requires some scope for fully qualified name...
        string ResolveFunction(string functionName)
        {
            return functionName;
        }

        // Result we get back from POST to api/run?func={0}
        public class BeginRunResult
        {
            public Guid Instance { get; set; }
        }

        private string MakeUri(string function, IDictionary<string, string> parameters)
        {
            // $$$ What about escaping and encoding?
            StringBuilder sb = new StringBuilder();
            sb.Append(_serviceUri);
            sb.Append("api/run/");
            sb.AppendFormat("?func={0}", function);

            foreach (var kv in parameters)
            {
                sb.AppendFormat("&{0}={1}", kv.Key, kv.Value);
            }
            return sb.ToString();
        }
         
        // Return task that is signaled 
        protected override Task WaitOnCall(Guid g)
        {
            // $$$ Do it synchronously. 
            throw new NotImplementedException();
        }
    }
}
