using System;
using System.Reflection;

namespace Microsoft.Azure.Jobs
{
    /// <summary>
    /// This will defer binding errors that happen before the invocation to resurface just before invocation, 
    /// when the mechanics of error handling and logging are properly set.
    /// </summary>
    internal class FailedParameterRuntimeBinding : ParameterRuntimeBinding
    {
        public string BindingErrorMessage { get; set; }

        public override string ConvertToInvokeString()
        {
            return "[binding error]";
        }

        public override BindResult Bind(IConfiguration config, IBinderEx bindingContext, ParameterInfo targetParameter)
        {
            throw new InvalidOperationException(BindingErrorMessage);
        }
    }
}