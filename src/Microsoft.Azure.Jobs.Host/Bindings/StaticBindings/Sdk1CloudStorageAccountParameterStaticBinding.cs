using System;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.Bindings.StaticBindings
{
    internal class Sdk1CloudStorageAccountParameterStaticBinding : ParameterStaticBinding
    {
        public override ParameterRuntimeBinding Bind(IRuntimeBindingInputs inputs)
        {
            return new Sdk1CloudStorageAccountParameterRuntimeBinding { Name = Name };
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

        public override ParameterDescriptor ToParameterDescriptor()
        {
            return new CloudStorageAccountParameterDescriptor();
        }

        private class Sdk1CloudStorageAccountParameterRuntimeBinding : ParameterRuntimeBinding
        {
            public override string ConvertToInvokeString()
            {
                return null;
            }

            // Bind to Microsoft.WindowsAzure.CloudStorageAccount
            public override BindResult Bind(IConfiguration config, IBinderEx bindingContext, ParameterInfo targetParameter)
            {
                Type cloudStorageAccountType = targetParameter.ParameterType;
                var res = Parse(cloudStorageAccountType, bindingContext.AccountConnectionString);
                return new BindResult { Result = res };
            }

            private static object Parse(Type cloudStorageAccountType, string accountConnectionString)
            {
                // call CloudStorageAccount.Parse(acs);
                var m = cloudStorageAccountType.GetMethod("Parse", BindingFlags.Static | BindingFlags.Public);
                var res = m.Invoke(null, new object[] { accountConnectionString });
                return res;
            }
        }
    }
}
