using System.Reflection;

namespace Microsoft.Azure.Jobs.Host.Bindings.StaticBindings
{
    internal class CancellationTokenParameterStaticBinding : ParameterStaticBinding
    {
        public override ParameterRuntimeBinding Bind(IRuntimeBindingInputs inputs)
        {
            return new CancellationTokenParameterRuntimeBinding { Name = Name };
        }

        public override ParameterRuntimeBinding BindFromInvokeString(IRuntimeBindingInputs inputs, string invokeString)
        {
            return null;
        }

        public override string Description
        {
            get { return null; }
        }

        public override string DefaultValue
        {
            get { return null; }
        }

        public override string Prompt
        {
            get { return null; }
        }

        private class CancellationTokenParameterRuntimeBinding : ParameterRuntimeBinding
        {
            public override string ConvertToInvokeString()
            {
                return null;
            }

            public override BindResult Bind(IConfiguration config, IBinderEx bindingContext, ParameterInfo targetParameter)
            {
                return new BindResult
                {
                    Result = bindingContext.CancellationToken
                };
            }
        }
    }
}
