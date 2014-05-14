using System;
using System.Reflection;

namespace Microsoft.Azure.Jobs.Host.Bindings.StaticBindingProviders
{
    internal class AttributeStaticBindingProvider : IStaticBindingProvider
    {
        public ParameterStaticBinding TryBind(ParameterInfo parameter)
        {
            foreach (Attribute attr in parameter.GetCustomAttributes(true))
            {
                ParameterStaticBinding staticBinding;
                try
                {
                    staticBinding = StaticBinder.DoStaticBind(attr, parameter);
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
