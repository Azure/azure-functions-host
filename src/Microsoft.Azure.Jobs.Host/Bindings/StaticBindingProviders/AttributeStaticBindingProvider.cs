using System;
using System.Reflection;

namespace Microsoft.Azure.Jobs.Host.Bindings.StaticBindingProviders
{
    internal class AttributeStaticBindingProvider : IStaticBindingProvider
    {
        public ParameterStaticBinding TryBind(ParameterInfo parameter, INameResolver nameResolver)
        {
            foreach (Attribute attr in parameter.GetCustomAttributes(true))
            {
                ParameterStaticBinding staticBinding;
                try
                {
                    var binder = new StaticBinder(nameResolver); 
                    staticBinding = binder.DoStaticBind(attr, parameter);
                }
                catch (Exception e)
                {
                    throw IndexException.NewParameter(parameter, e);
                }
                if (staticBinding != null)
                {
                    return staticBinding;
                }
            }

            return null;
        }
    }
}
