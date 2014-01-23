using System;

namespace Microsoft.WindowsAzure.Jobs
{
    // BinderProvider for adding an ICall binder. 
    // This binds to a FunctionInvoker object, which doesn't actually implement ICall, so
    // this adds a wrapper.
    internal class CallBinderProvider : ICloudBinderProvider, ICloudBinder
    {
        private ICall _inner;
                
        public static void Insert(IConfiguration config, ICall inner)
        {
            config.Binders.Insert(0, new CallBinderProvider { _inner = inner });
        }

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
            return new BindResult            
            { 
                Result = new CallWithContextWrapper(_inner, bindingContext.FunctionInstanceGuid)
            };
        }
    }
}
