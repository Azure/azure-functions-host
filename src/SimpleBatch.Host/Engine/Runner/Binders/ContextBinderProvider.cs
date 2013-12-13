using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SimpleBatch;

namespace RunnerHost
{
    // Binder for IContext. 
    internal class ContextBinderProvider : ICloudBinderProvider
    {
        public ICloudBinder TryGetBinder(Type targetType)
        {
            if (targetType == typeof(IContext))
            {
                return new ContextBinder();
            }
            return null;
        }

        class ContextBinder : ICloudBinder
        {
            public BindResult Bind(IBinderEx bindingContext, System.Reflection.ParameterInfo parameter)
            {
                var g = bindingContext.FunctionInstanceGuid;
                return new BindResult<IContext>(new Context { FunctionInstanceGuid = g });
            }
        }

        class Context : IContext
        {
            public Guid FunctionInstanceGuid { get; set; }            
        }
    }
}
