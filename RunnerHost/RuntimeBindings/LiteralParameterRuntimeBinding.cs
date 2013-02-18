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
    public class UnknownParameterRuntimeBinding : ParameterRuntimeBinding
    {
        public string AccountConnectionString { get; set; }
        public override string ConvertToInvokeString()
        {
            return string.Empty;
        }

        public override BindResult Bind(IConfiguration config, IBinder bindingContext, ParameterInfo targetParameter)
        {
            var t = targetParameter.ParameterType;
            var binder = config.GetBinder(t);
            if (binder == null)
            {
                string msg = string.Format("Can't bind parameter '{0}' to type '{1}'. Are you missing a custom model binder?", targetParameter.Name, t);
                throw new InvalidOperationException(msg);
            }

            // use the supplied account connection string
            var ctx2 = new BindingContext(config, this.AccountConnectionString); 

            return binder.Bind(ctx2, targetParameter);
        }
    }

    // Literal string. Similar to LiteralObjectParameterRuntimeBinding in that they're both literals.
    // Just that the format is different. This is just a string, and not necesasrily json.
    // $$$ Merge with LiteralObjectParameterRuntimeBinding? That just puts pressure on the encoder.
    public class LiteralStringParameterRuntimeBinding : ParameterRuntimeBinding
    {
        public string Value { get; set; }

        public override BindResult Bind(IConfiguration config, IBinder bindingContext, ParameterInfo targetParameter)
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

        public override BindResult Bind(IConfiguration config, IBinder bindingContext, ParameterInfo targetParameter)
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