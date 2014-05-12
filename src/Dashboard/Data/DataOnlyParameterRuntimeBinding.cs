using System;
using Microsoft.Azure.Jobs;

namespace Dashboard.Data
{
    internal class DataOnlyParameterRuntimeBinding : ParameterRuntimeBinding
    {
        public string Value { get; set; }

        public bool? IsBlob { get; set; }

        public bool? IsBlobInput { get; set; }

        public DataOnlyParameterRuntimeBinding(string name, string value, bool? isBlob, bool? isBlobInput)
        {
            Name = name;
            Value = value;
            IsBlob = isBlob;
            IsBlobInput = isBlobInput;
        }

        public override string ConvertToInvokeString()
        {
            return Value;
        }

        public override BindResult Bind(IConfiguration config, IBinderEx bindingContext, System.Reflection.ParameterInfo targetParameter)
        {
            throw new NotSupportedException();
        }
    }
}