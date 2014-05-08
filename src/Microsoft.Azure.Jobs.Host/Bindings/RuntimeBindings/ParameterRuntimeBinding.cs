using System;
using System.Reflection;
using Newtonsoft.Json;

namespace Microsoft.Azure.Jobs
{
    // Represents a parameter instance, used to invoke an instance of a function.
    // This can be serialized and stored in the payload of a execution request. 
    // In the runner host process, it gets converted into a System.Object for finally invoking a function.    
    // This can do lots of heavy stuff in the binder, like converting a Blob to 18 different runtime types.     
    // This Serializes to JSON. 
    internal abstract class ParameterRuntimeBinding
    {
        // Get a "human readable" string that can be displayed and passed to BindFromString
        // This can be part of the UI. 
        public abstract string ConvertToInvokeString();

        // Get a runtime object. 
        // Include ParameterInfo because those don't serialize. 
        // Also, the parameterInfo provides a loader-approved System.Type for the target parameter, so
        // that avoids trying to serialize and rehydrate a type.
        // This should be the same parameter info that the function was originally indexed against. 
        public abstract BindResult Bind(IConfiguration config, IBinderEx bindingContext, ParameterInfo targetParameter);

        public override string ToString()
        {
            return this.ConvertToInvokeString();
        }
    }
}
