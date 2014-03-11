using System;
using System.Reflection;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class UnknownInvokeParameterRuntimeBinding : ParameterRuntimeBinding
    {
        public string AccountConnectionString { get; set; }
        public string Value { get; set; }

        public override string ConvertToInvokeString()
        {
            return Value;
        }

        public override BindResult Bind(IConfiguration config, IBinderEx bindingContext, ParameterInfo targetParameter)
        {
            // Work around problem using IBinder and CloudStorageAccount with Run/Replay from dashboard.
            if (RunnerProgram.ShouldIgnoreInvokeString(targetParameter.ParameterType))
            {
                ParameterRuntimeBinding bindingWithoutInvokeString = new UnknownParameterRuntimeBinding
                {
                    AccountConnectionString = AccountConnectionString
                };
                return bindingWithoutInvokeString.Bind(config, bindingContext, targetParameter);
            }

            ICloudBinder binder = TryGetBinder(config, targetParameter);

            if (binder != null)
            {
                // For bindings to attributes, always use the attribute-based binder.
                return UnknownParameterRuntimeBinding.Bind(binder, AccountConnectionString, bindingContext, targetParameter);
            }
            else
            {
                // For other bindings, convert from the invoke string.
                return new LiteralStringParameterRuntimeBinding { Value = Value }.Bind(config, bindingContext, targetParameter);
            }
        }

        public static ICloudBinder TryGetBinder(IConfiguration config, ParameterInfo targetParameter)
        {
            var t = targetParameter.ParameterType;
            return config.GetBinder(t);
        }
    }
}
