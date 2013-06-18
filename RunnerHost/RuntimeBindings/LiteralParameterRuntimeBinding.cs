using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;
using SimpleBatch;

namespace RunnerHost
{
    // Literal string. Similar to LiteralObjectParameterRuntimeBinding in that they're both literals.
    // Just that the format is different. This is just a string, and not necesasrily json.
    // $$$ Merge with LiteralObjectParameterRuntimeBinding? That just puts pressure on the encoder.
    public class LiteralStringParameterRuntimeBinding : ParameterRuntimeBinding
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


    // Literal object.
    // The input could have come from someplace interesting (such as a queue input payload),
    // the point is that by the time we have a function invocation, the object is fixed. 
    public class LiteralObjectParameterRuntimeBinding : ParameterRuntimeBinding
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