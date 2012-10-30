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
    // Bind to ICall
    public class CallBinderProvider : ICloudBinderProvider, ICloudBinder
    {
        public static CallBinderProvider New(Func<FunctionInvoker> fpNewInvoker)
        {
            return new CallBinderProvider { _fpNewInvoker = fpNewInvoker }; 
        }

        // Insert a binding for ICall to the result supplied by fpNewInvoker().
        public static void Insert(Func<FunctionInvoker> fpNewInvoker, IConfiguration config)
        {
            config.Binders.Insert(0, New(fpNewInvoker));
        }

        private Func<FunctionInvoker> _fpNewInvoker;

        public ICloudBinder TryGetBinder(Type targetType)
        {
            if (targetType == typeof(ICall))
            {
                return this;
            }
            return null;
        }

        BindResult ICloudBinder.Bind(IBinder bindingContext, System.Reflection.ParameterInfo parameter)
        {
            var result = _fpNewInvoker();

            return new BindResultTransaction
            {
                Result = new WebCallWrapper { _inner = result },
                Cleanup = () =>
                {
                    result.Flush();
                }
            };
        }
    }

    // Adapter to expose interfaces on a web invoker.
    class WebCallWrapper : ICall, ISelfWatch
    {
        public FunctionInvoker _inner;

        public void QueueCall(string functionName, object arguments = null)
        {
            _inner.QueueCall(functionName, arguments);
        }

        public string GetStatus()
        {
            return _inner.GetStatus();
        }
    }
}
