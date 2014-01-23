using System.Reflection;

namespace Microsoft.WindowsAzure.Jobs
{
    // Interface to provide static validation for ICloudBinder during static binding.
    // It provides an ParameterInfo, which static binding can use for additional error checks.
    // It gives an ICloudBinder a chance to fail on things like illegal names. 
    internal interface ICloudBinderVerify
    {
        // Throw an exception if the binding is bad. 
        void Validate(ParameterInfo parameter);
    }
}
