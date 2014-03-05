using System.Reflection;

namespace Microsoft.WindowsAzure.Jobs
{
    // Literal string. Similar to LiteralObjectParameterRuntimeBinding in that they're both literals.
    // Just that the format is different. This is just a string, and not necesasrily json.
    // $$$ Merge with LiteralObjectParameterRuntimeBinding? That just puts pressure on the encoder.
    internal class LiteralStringParameterRuntimeBinding : ParameterRuntimeBinding
    {
        public string Value { get; set; }
        public string AccountConnectionString { get; set; }

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

            var result = ObjectBinderHelpers.BindFromString(Value, targetParameter.ParameterType);
            return new BindResult { Result = result };
        }

        public override string ConvertToInvokeString()
        {
            return Value;
        }
    }
}
