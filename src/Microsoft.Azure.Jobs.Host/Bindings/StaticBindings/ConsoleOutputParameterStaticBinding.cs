using System.Reflection;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Bindings.StaticBindings
{
    internal class ConsoleOutputParameterStaticBinding : ParameterStaticBinding
    {
        public override ParameterRuntimeBinding Bind(IRuntimeBindingInputs inputs)
        {
            return new ConsoleOutputParameterRuntimeBinding { Name = Name };
        }

        public override ParameterRuntimeBinding BindFromInvokeString(IRuntimeBindingInputs inputs, string invokeString)
        {
            return null;
        }

        public override ParameterDescriptor ToParameterDescriptor()
        {
            return new ConsoleOutputParameterDescriptor();
        }

        private class ConsoleOutputParameterRuntimeBinding : ParameterRuntimeBinding
        {
            public override string ConvertToInvokeString()
            {
                return null;
            }

            public override BindResult Bind(IConfiguration config, IBinderEx bindingContext, ParameterInfo targetParameter)
            {
                return new BindResult
                {
                    Result = bindingContext.ConsoleOutput
                };
            }
        }
    }
}
