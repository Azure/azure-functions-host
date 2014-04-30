using System.Reflection;

namespace Microsoft.Azure.Jobs
{
    // Literal string. Similar to LiteralObjectParameterRuntimeBinding in that they're both literals.
    // Just that the format is different. This is just a string, and not necesasrily json.
    // $$$ Merge with LiteralObjectParameterRuntimeBinding? That just puts pressure on the encoder.
    internal class LiteralStringParameterRuntimeBinding : ParameterRuntimeBinding
    {
        public string Value { get; set; }

        public override BindResult Bind(IConfiguration config, IBinderEx bindingContext, ParameterInfo targetParameter)
        {
            var result = ObjectBinderHelpers.BindFromString(Value, targetParameter.ParameterType);
            return new BindResult { Result = result };
        }

        public override string ConvertToInvokeString()
        {
            return Value;
        }
    }
}
