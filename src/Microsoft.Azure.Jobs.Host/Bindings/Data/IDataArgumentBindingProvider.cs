using System.Reflection;

namespace Microsoft.Azure.Jobs.Host.Bindings.Data
{
    internal interface IDataArgumentBindingProvider<TBindingData>
    {
        IArgumentBinding<TBindingData> TryCreate(ParameterInfo parameter);
    }
}
