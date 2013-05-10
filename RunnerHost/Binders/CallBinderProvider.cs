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
    // BinderProvider for adding an ICall binder. 
    // This binds to a FunctionInvoker object, which doesn't actually implement ICall, so
    // this adds a wrapper.
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

        BindResult ICloudBinder.Bind(IBinderEx bindingContext, System.Reflection.ParameterInfo parameter)
        {
            var result = _fpNewInvoker();

            return new BindResultTransaction
            {
                Result = new WebCallWrapper(result, bindingContext.FunctionInstanceGuid),
                Cleanup = () =>
                {
                    // !!! skip
                }
            };
        }
    }
}