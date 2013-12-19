using System.Reflection;

namespace Microsoft.WindowsAzure.Jobs
{
    // Literal object.
    // The input could have come from someplace interesting (such as a queue input payload),
    // the point is that by the time we have a function invocation, the object is fixed. 
    internal class LiteralObjectParameterRuntimeBinding : ParameterRuntimeBinding
    {
        public string LiteralJson { get; set; }

        public override BindResult Bind(IConfiguration config, IBinderEx bindingContext, ParameterInfo targetParameter)
        {
            object result = JsonCustom.DeserializeObject(this.LiteralJson, targetParameter.ParameterType);
            return new BindResult { Result = result };
        }

        public override string ConvertToInvokeString()
        {
            return LiteralJson;
        }
    }
}
