using System.Reflection;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.Bindings.StaticBindings
{
    internal class CloudStorageAccountParameterStaticBinding : ParameterStaticBinding
    {
        public override ParameterRuntimeBinding Bind(IRuntimeBindingInputs inputs)
        {
            return new CloudStorageAccountParameterRuntimeBinding { Name = Name };
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

        private class CloudStorageAccountParameterRuntimeBinding : ParameterRuntimeBinding
        {
            public override string ConvertToInvokeString()
            {
                return null;
            }

            public override BindResult Bind(IConfiguration config, IBinderEx bindingContext, ParameterInfo targetParameter)
            {
                CloudStorageAccount account = Utility.GetAccount(bindingContext.AccountConnectionString);
                return new BindResult { Result = account };
            }
        }
    }
}
