using System.Reflection;

namespace Microsoft.WindowsAzure.Jobs
{
    // Interface to provide static validation for ICloudBinder
    // $$$ This is only needed by custom cloud binders. Should we move this to SimpleBatch.dll? Or some other common place?
    public interface ICloudBinderVerify
    {
        void Validate(ParameterInfo parameter);
    }
}