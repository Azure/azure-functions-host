using System.IO;
using System.Reflection;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal interface IBlobArgumentBindingProvider
    {
        IBlobArgumentBinding TryCreate(ParameterInfo parameter, FileAccess? access);
    }
}
