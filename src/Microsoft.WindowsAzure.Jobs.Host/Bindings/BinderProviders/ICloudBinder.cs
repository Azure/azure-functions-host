using System.Reflection;

namespace Microsoft.WindowsAzure.Jobs
{
    // Binds to arbitrary entities in the cloud
    internal interface ICloudBinder
    {
        BindResult Bind(IBinderEx bindingContext, ParameterInfo parameter);
    }
}
